using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WolfNETRadio.Audio;
using WolfNETRadio.Models;
using WolfNETRadio.Network;

namespace WolfNETRadio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ClientState             _state;
    private readonly SRSControlClient        _control;
    private readonly SRSVoiceClient          _voice;
    private readonly GwReconAuthClient       _auth;
    private readonly LinuxAudioInputManager  _audioIn;
    private readonly LinuxAudioOutputManager _audioOut;
    private readonly OpusProcessor           _opus;

    [ObservableProperty] private float  _vuLevel;
    [ObservableProperty] private string _statusMessage = "Ready";

    private readonly HashSet<int> _activeTransmitRadios = [];
    private readonly Dictionary<int, System.Timers.Timer> _voxHangTimers = [];

    public MainViewModel(
        ClientState state, SRSControlClient control, SRSVoiceClient voice,
        GwReconAuthClient auth,
        LinuxAudioInputManager audioIn, LinuxAudioOutputManager audioOut,
        OpusProcessor opus)
    {
        _state   = state;
        _control = control;
        _voice   = voice;
        _auth    = auth;
        _audioIn  = audioIn;
        _audioOut = audioOut;
        _opus     = opus;

        _voice.AudioReceived += OnAudioReceived;
        _audioIn.FrameReady  += OnMicFrame;
        _audioIn.VuLevel     += level => VuLevel = level;
        _state.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }

    public string ServerAddress
    {
        get => _state.ServerAddress;
        set { _state.ServerAddress = value; OnPropertyChanged(); }
    }
    public string RadioAccessKey
    {
        get => _state.RadioAccessKey;
        set { _state.RadioAccessKey = value; OnPropertyChanged(); }
    }
    public bool   IsConnected           => _state.IsConnected;
    public bool   IsVoipConnected       => _state.IsVoipConnected;
    public int    ConnectedClientCount  => _state.ConnectedClientCount;
    public string? SelectedInputDevice
    {
        get => _state.InputDeviceName;
        set { _state.InputDeviceName = value ?? string.Empty; OnPropertyChanged(); }
    }
    public string? SelectedOutputDevice
    {
        get => _state.OutputDeviceName;
        set { _state.OutputDeviceName = value ?? string.Empty; OnPropertyChanged(); }
    }
    public float VoxThreshold
    {
        get => _state.VoxThreshold;
        set { _state.VoxThreshold = value; OnPropertyChanged(); }
    }

    // ── PTT from LinuxPTTManager ─────────────────────────────────────────────

    public void OnPTTState(int radioId, bool pressed)
    {
        if (radioId < 0 || radioId >= _state.Radios.Length) return;
        _state.Radios[radioId].IsTransmitting = pressed;
        if (pressed) _activeTransmitRadios.Add(radioId);
        else         _activeTransmitRadios.Remove(radioId);
    }

    // ── Connect ──────────────────────────────────────────────────────────────

    public async Task ConnectAsync()
    {
        StatusMessage = "Authenticating…";
        if (!string.IsNullOrWhiteSpace(_state.RadioAccessKey))
        {
            var auth = await _auth.ValidateKeyAsync(_state.RadioAccessKey);
            if (auth.Valid)
            {
                _state.Callsign = auth.Callsign;
                _state.Rank     = auth.Rank;
                _state.AuthValid = true;
            }
            else if (!auth.Error!.Contains("connect", StringComparison.OrdinalIgnoreCase) &&
                     !auth.Error!.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Auth failed: {auth.Error}";
                _state.ServerStatus = ConnectionStatus.Error;
                return;
            }
        }

        var parts = _state.ServerAddress.Split(':');
        var host  = parts[0].Trim();
        var port  = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5002;
        StatusMessage = $"Connecting to {host}:{port}…";

        await _control.ConnectAsync(host, port);
        _voice.Connect(host, port);
        _state.VoipStatus = ConnectionStatus.Connected;
        _audioOut.Start(_state.OutputDeviceName);
        _audioIn.StartCapture(_state.InputDeviceName, _state.MicVolume);
        StatusMessage = $"Connected as {_state.Callsign}";
    }

    public void Disconnect()
    {
        _audioIn.StopCapture();
        _audioOut.Stop();
        _voice.Disconnect();
        _control.Disconnect();
        _state.VoipStatus  = ConnectionStatus.Disconnected;
        _state.AuthValid   = false;
        StatusMessage = "Disconnected";
    }

    // ── Audio received ────────────────────────────────────────────────────────

    private void OnAudioReceived(float[] pcm, string fromGuid, double[] frequencies)
    {
        // Match frequency to a radio slot
        int matchedSlot = -1;
        float pan = 0.0f;
        for (int i = 1; i <= 10; i++)
        {
            var slot = _state.Radios[i];
            if (!slot.IsActive) continue;
            foreach (var freq in frequencies)
            {
                if (Math.Abs(slot.FrequencyHz - freq) < 500.0)
                {
                    matchedSlot = i;
                    pan = slot.Pan switch
                    {
                        RadioSlot.RadioPan.Left  => -1.0f,
                        RadioSlot.RadioPan.Right => +1.0f,
                        _ => 0.0f
                    };
                    break;
                }
            }
            if (matchedSlot >= 0) break;
        }

        _audioOut.PlayAudio(fromGuid, pan, pcm);

        if (matchedSlot >= 0)
        {
            var slot = _state.Radios[matchedSlot];
            slot.IsReceiving       = true;
            slot.ReceivingCallsign = _control.GetClientName(fromGuid);
            _ = ClearReceiveAfterDelay(matchedSlot, 500);
        }
    }

    private async Task ClearReceiveAfterDelay(int slotIdx, int ms)
    {
        await Task.Delay(ms);
        var slot = _state.Radios[slotIdx];
        slot.IsReceiving       = false;
        slot.ReceivingCallsign = string.Empty;
    }

    // ── Mic / VOX ─────────────────────────────────────────────────────────────

    private void OnMicFrame(float[] pcm)
    {
        if (_audioOut.IsPassthroughActive) _audioOut.PlayPassthrough(pcm);
        if (_activeTransmitRadios.Count > 0)
            _voice.SendAudio(pcm, _activeTransmitRadios.ToArray());

        // VOX processing
        float rms      = ComputeRms(pcm);
        VuLevel        = rms;
        bool voxActive = rms > _state.VoxThreshold;
        for (int i = 0; i <= 10; i++)
        {
            var slot = _state.Radios[i];
            if (slot.Mode != RadioSlot.RadioMode.VOX) continue;
            HandleVox(i, voxActive);
        }
    }

    private void HandleVox(int radioId, bool triggered)
    {
        if (triggered)
        {
            if (_voxHangTimers.TryGetValue(radioId, out var t)) { t.Stop(); t.Dispose(); _voxHangTimers.Remove(radioId); }
            if (!_state.Radios[radioId].IsTransmitting)
            {
                _state.Radios[radioId].IsTransmitting = true;
                _activeTransmitRadios.Add(radioId);
            }
        }
        else
        {
            if (!_state.Radios[radioId].IsTransmitting) return;
            if (_voxHangTimers.ContainsKey(radioId)) return;
            var t = new System.Timers.Timer(_state.VoxHangtimeMs) { AutoReset = false };
            t.Elapsed += (_, _) =>
            {
                _state.Radios[radioId].IsTransmitting = false;
                _activeTransmitRadios.Remove(radioId);
                if (_voxHangTimers.TryGetValue(radioId, out var timer)) { timer.Dispose(); _voxHangTimers.Remove(radioId); }
            };
            _voxHangTimers[radioId] = t;
            t.Start();
        }
    }

    private static float ComputeRms(float[] pcm)
    {
        float sum = 0; foreach (var s in pcm) sum += s * s;
        return MathF.Sqrt(sum / pcm.Length);
    }
}
