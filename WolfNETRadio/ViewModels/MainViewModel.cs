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

        // Wire received audio → output
        _voice.AudioReceived += (pcm, guid) => _audioOut.PlayAudio(guid, pcm);

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

    private void OnPTTStateChanged(int radioId, bool pressed)
    {
        if (radioId >= 0 && radioId < _state.Radios.Length)
            _state.Radios[radioId].IsTransmitting = pressed;

        if (pressed) _activeTransmitRadios.Add(radioId);
        else _activeTransmitRadios.Remove(radioId);
    }

    private void OnMicFrame(float[] pcm)
    {
        if (_activeTransmitRadios.Count == 0) return;
        _voice.SendAudio(pcm, _activeTransmitRadios.ToArray());
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
}
