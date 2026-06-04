using CommunityToolkit.Mvvm.ComponentModel;

namespace WolfNETRadio.Models;

public enum ConnectionStatus { Disconnected, Connecting, Connected, Error }

public partial class ClientState : ObservableObject
{
    public static readonly ClientState Instance = new();

    // Connection
    [ObservableProperty] private ConnectionStatus _serverStatus = ConnectionStatus.Disconnected;
    [ObservableProperty] private ConnectionStatus _voipStatus = ConnectionStatus.Disconnected;
    [ObservableProperty] private string _serverAddress = "gwrecon.com:5002";
    [ObservableProperty] private string _radioAccessKey = string.Empty;
    [ObservableProperty] private string _callsign = "UNKNOWN";
    [ObservableProperty] private int _rank;
    [ObservableProperty] private bool _authValid;

    // Radios: index 0=intercom, 1-10=ops
    public RadioSlot[] Radios { get; } = Enumerable.Range(0, 11)
        .Select(i => new RadioSlot { RadioId = i, Label = i == 0 ? "INTERCOM" : $"CH-{i}" })
        .ToArray();

    // Connected clients on server
    [ObservableProperty] private int _connectedClientCount;

    // Overlay visibility
    [ObservableProperty] private bool _commsOverlayVisible;
    [ObservableProperty] private bool _commandCenterVisible;

    // Settings
    [ObservableProperty] private double _overlayOpacity = 1.0;
    [ObservableProperty] private string _inputDeviceName = string.Empty;
    [ObservableProperty] private string _outputDeviceName = string.Empty;
    [ObservableProperty] private float _micVolume = 1.0f;
    [ObservableProperty] private float _speakerVolume = 1.0f;
    [ObservableProperty] private bool _radioEffectsEnabled = true;

    public bool IsConnected => ServerStatus == ConnectionStatus.Connected;
    public bool IsVoipConnected => VoipStatus == ConnectionStatus.Connected;

    private ClientState() { }
}
