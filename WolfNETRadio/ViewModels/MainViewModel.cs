using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WolfNETRadio.Audio;
using WolfNETRadio.Input;
using WolfNETRadio.Models;
using WolfNETRadio.Network;

namespace WolfNETRadio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ClientState _state;
    private readonly SRSControlClient _control;
    private readonly SRSVoiceClient _voice;
    private readonly GwReconAuthClient _auth;
    private readonly AudioInputManager _audioIn;
    private readonly AudioOutputManager _audioOut;
    private readonly OpusProcessor _opus;
    private readonly PTTManager _ptt;

    public MainViewModel(
        ClientState state, SRSControlClient control, SRSVoiceClient voice,
        GwReconAuthClient auth, AudioInputManager audioIn, AudioOutputManager audioOut,
        OpusProcessor opus, PTTManager ptt)
    {
        _state = state;
        _control = control;
        _voice = voice;
        _auth = auth;
        _audioIn = audioIn;
        _audioOut = audioOut;
        _opus = opus;
        _ptt = ptt;

        // Wire PTT → voice send
        _ptt.PTTStateChanged += OnPTTStateChanged;

        // Wire channel switch
        _ptt.ChannelSwitchRequested += radioId =>
        {
            if (radioId >= 1 && radioId <= 10)
                _state.Radios[radioId].SwitchChannel();
        };

        // Wire received audio → output
        _voice.AudioReceived += OnAudioReceived;

        // Wire mic frames → encode + send when PTT active
        _audioIn.FrameReady += OnMicFrame;
        _audioIn.VuLevel += level => VuLevel = level;

        // Mirror state properties
        _state.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

        // Build radio slot VMs
        RadioSlots = _state.Radios.Skip(1).Take(10)
            .Select(r => new RadioSlotViewModel(r, _control))
            .ToArray();
        IntercomSlot = new RadioSlotViewModel(_state.Radios[0], _control);
    }

    public RadioSlotViewModel[] RadioSlots { get; }
    public RadioSlotViewModel IntercomSlot { get; }

    [ObservableProperty] private float _vuLevel;
    [ObservableProperty] private string _statusMessage = "Ready";

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

    public bool IsConnected => _state.IsConnected;
    public bool IsVoipConnected => _state.IsVoipConnected;
    public ConnectionStatus ServerStatus => _state.ServerStatus;
    public ConnectionStatus VoipStatus => _state.VoipStatus;
    public int ConnectedClientCount => _state.ConnectedClientCount;

    public IEnumerable<string> InputDevices => _audioIn.GetInputDevices();
    public IEnumerable<string> OutputDevices => _audioOut.GetOutputDevices();

    public bool RadioEffectsEnabled
    {
        get => _state.RadioEffectsEnabled;
        set { _state.RadioEffectsEnabled = value; OnPropertyChanged(); }
    }

    public float SpeakerVolume
    {
        get => _state.SpeakerVolume;
        set { _state.SpeakerVolume = value; OnPropertyChanged(); }
    }

    public float MicVolume
    {
        get => _state.MicVolume;
        set { _state.MicVolume = value; OnPropertyChanged(); }
    }

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

    [RelayCommand]
    public async Task ConnectAsync()
    {
        try
        {
            // 1. Validate Radio Access Key (fail-open if gwrecon unreachable)
            string callsign = _state.Callsign;
            if (!string.IsNullOrWhiteSpace(_state.RadioAccessKey))
            {
                StatusMessage = "Authenticating...";
                var authResult = await _auth.ValidateKeyAsync(_state.RadioAccessKey);
                if (authResult.Valid)
                {
                    callsign = authResult.Callsign;
                    _state.Callsign = callsign;
                    _state.Rank = authResult.Rank;
                    _state.AuthValid = true;
                }
                else if (authResult.Error != null && (
                    authResult.Error.Contains("connect", System.StringComparison.OrdinalIgnoreCase) ||
                    authResult.Error.Contains("timeout", System.StringComparison.OrdinalIgnoreCase) ||
                    authResult.Error.Contains("unreachable", System.StringComparison.OrdinalIgnoreCase)))
                {
                    StatusMessage = "Auth server unreachable — connecting anyway...";
                }
                else
                {
                    StatusMessage = $"Auth failed: {authResult.Error ?? "Invalid key"}";
                    _state.ServerStatus = ConnectionStatus.Error;
                    return;
                }
            }

            // 2. Parse host:port
            var parts = _state.ServerAddress.Split(':');
            var host = parts[0].Trim();
            var port = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var p) ? p : 5002;

            StatusMessage = $"Connecting to {host}:{port}...";

            // 3. TCP control connect
            await _control.ConnectAsync(host, port);

            // 4. UDP voice connect
            _voice.Connect(host, port);
            _state.VoipStatus = ConnectionStatus.Connected;

            // 5. Start audio
            _audioOut.Start(_state.OutputDeviceName);
            _audioIn.StartCapture(_state.InputDeviceName, _state.MicVolume);

            // 6. Install PTT hook
            _ptt.InstallHook();

            StatusMessage = $"Connected as {callsign}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _state.ServerStatus = ConnectionStatus.Error;
        }
    }

    [RelayCommand]
    public void Disconnect()
    {
        _ptt.Dispose();
        _audioIn.StopCapture();
        _audioOut.Stop();
        _voice.Disconnect();
        _control.Disconnect();
        _state.VoipStatus = ConnectionStatus.Disconnected;
        _state.AuthValid = false;
        StatusMessage = "Disconnected";
    }

    private readonly HashSet<int> _activeTransmitRadios = [];
    private readonly Dictionary<int, System.Timers.Timer> _voxHangTimers = [];

    private void OnPTTStateChanged(int radioId, bool pressed)
    {
        if (radioId >= 0 && radioId < _state.Radios.Length)
            _state.Radios[radioId].IsTransmitting = pressed;

        if (pressed) _activeTransmitRadios.Add(radioId);
        else _activeTransmitRadios.Remove(radioId);
    }

    private void OnAudioReceived(float[] pcm, string fromGuid, double[] frequencies)
    {
        // Find which radio slot this frequency matches
        int matchedSlot = -1;
        float pan = 0.0f;
        for (int i = 1; i <= 10; i++)
        {
            var slot = _state.Radios[i];
            if (!slot.IsActive) continue;
            foreach (var freq in frequencies)
            {
                if (Math.Abs(slot.FrequencyHz - freq) < 500.0)  // within 500Hz
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

        // Play audio with pan
        _audioOut.PlayAudio(fromGuid, pan, pcm);

        // Update RX status
        if (matchedSlot >= 0)
        {
            var slot = _state.Radios[matchedSlot];
            slot.IsReceiving = true;
            var name = _control.GetClientName(fromGuid);
            slot.ReceivingCallsign = name;
            // Clear after 500ms of no packets
            _ = ClearReceiveAfterDelay(matchedSlot, fromGuid, 500);
        }
    }

    private async Task ClearReceiveAfterDelay(int slotIdx, string guid, int ms)
    {
        await Task.Delay(ms);
        if (slotIdx >= 0 && slotIdx < _state.Radios.Length)
        {
            var slot = _state.Radios[slotIdx];
            slot.IsReceiving = false;
            slot.ReceivingCallsign = string.Empty;
        }
    }

    private void OnMicFrame(float[] pcm)
    {
        // Passthrough test
        if (_audioOut.IsPassthroughActive) _audioOut.PlayPassthrough(pcm);

        // PTT radios
        if (_activeTransmitRadios.Count > 0)
            _voice.SendAudio(pcm, _activeTransmitRadios.ToArray());

        // VOX radios
        var rms = ComputeRms(pcm);
        VuLevel = rms;
        var voxActive = rms > _state.VoxThreshold;
        for (int i = 0; i <= 10; i++)
        {
            var slot = _state.Radios[i];
            if (slot.Mode != RadioSlot.RadioMode.VOX) continue;
            HandleVoxState(i, voxActive);
        }
    }

    private void HandleVoxState(int radioId, bool triggered)
    {
        if (triggered)
        {
            if (_voxHangTimers.TryGetValue(radioId, out var t)) { t.Stop(); t.Dispose(); _voxHangTimers.Remove(radioId); }
            if (_state.Radios[radioId].IsTransmitting == false)
            {
                _state.Radios[radioId].IsTransmitting = true;
                _activeTransmitRadios.Add(radioId);
            }
        }
        else
        {
            if (!_state.Radios[radioId].IsTransmitting) return;
            if (!_voxHangTimers.ContainsKey(radioId))
            {
                var t = new System.Timers.Timer(_state.VoxHangtimeMs) { AutoReset = false };
                t.Elapsed += (_, _) =>
                {
                    _state.Radios[radioId].IsTransmitting = false;
                    _activeTransmitRadios.Remove(radioId);
                    _voxHangTimers.Remove(radioId);
                };
                _voxHangTimers[radioId] = t;
                t.Start();
            }
        }
    }

    private static float ComputeRms(float[] pcm)
    {
        float sum = 0;
        foreach (var s in pcm) sum += s * s;
        return MathF.Sqrt(sum / pcm.Length);
    }

    public bool CommsOverlayVisible
    {
        get => _state.CommsOverlayVisible;
        set { _state.CommsOverlayVisible = value; OnPropertyChanged(); }
    }

    public bool CommandCenterVisible
    {
        get => _state.CommandCenterVisible;
        set { _state.CommandCenterVisible = value; OnPropertyChanged(); }
    }

    public float VoxThreshold
    {
        get => _state.VoxThreshold;
        set { _state.VoxThreshold = value; OnPropertyChanged(); }
    }

    public int VoxHangtimeMs
    {
        get => _state.VoxHangtimeMs;
        set { _state.VoxHangtimeMs = value; OnPropertyChanged(); }
    }
}
