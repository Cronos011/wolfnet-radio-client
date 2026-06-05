using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using WolfNETRadio.Models;

namespace WolfNETRadio.Network;

public partial class SRSControlClient : ObservableObject, IDisposable
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private CancellationTokenSource? _cts;
    private readonly ClientState _state;
    public const string CLIENT_VERSION = "2.1.0.4";
    private readonly string _clientGuid = Guid.NewGuid().ToString();
    private readonly Dictionary<string, string> _clientNames = [];

    public event Action<List<SRSClient>>? ClientListUpdated;
    public event Action<Dictionary<string, string>>? ServerSettingsReceived;

    public SRSControlClient(ClientState state) => _state = state;

    public string GetClientName(string guid) => _clientNames.GetValueOrDefault(guid, guid[..Math.Min(8, guid.Length)]);

    public async Task ConnectAsync(string host, int port)
    {
        _state.ServerStatus = ConnectionStatus.Connecting;
        try
        {
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(host, port);
            _stream = _tcp.GetStream();
            _reader = new StreamReader(_stream, Encoding.UTF8);
            _state.ServerStatus = ConnectionStatus.Connected;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            _ = Task.Run(() => PingLoopAsync(_cts.Token));
            await SendUpdateAsync();
        }
        catch
        {
            _state.ServerStatus = ConnectionStatus.Error;
            throw;
        }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _tcp?.Close();
        _state.ServerStatus = ConnectionStatus.Disconnected;
    }

    public async Task SendUpdateAsync()
    {
        var msg = new SRSMessage
        {
            MsgType = SRSMessageType.Update,
            Version = CLIENT_VERSION,
            RadioAccessKey = _state.RadioAccessKey,
            Client = new SRSClient
            {
                ClientGuid = _clientGuid,
                Name = _state.Callsign,
                Coalition = 1,
                RadioInfo = SRSRadioInfo.FromClientState(_state),
                Seat = 0,
            },
        };

        await SendMessageAsync(msg);
    }

    private async Task SendMessageAsync(SRSMessage msg)
    {
        if (_stream == null) return;
        var json = SRSSerializer.Serialize(msg);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _stream.WriteAsync(bytes);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (_reader == null) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync();
                if (line == null) break;

                var msg = SRSSerializer.Deserialize(line);
                if (msg == null) continue;

                switch (msg.MsgType)
                {
                    case SRSMessageType.ServerSettings:
                        if (msg.ServerSettings != null)
                        {
                            UpdateStateFromSettings(msg.ServerSettings);
                            ServerSettingsReceived?.Invoke(msg.ServerSettings);
                        }
                        break;
                    case SRSMessageType.Sync:
                    case SRSMessageType.RadioUpdate:
                        if (msg.Clients != null)
                        {
                            _state.ConnectedClientCount = msg.Clients.Count;
                            UpdateTunedClientCounts(msg.Clients);
                            // Populate client names dictionary
                            foreach (var client in msg.Clients)
                                if (client.ClientGuid != null && client.Name != null)
                                    _clientNames[client.ClientGuid] = client.Name;
                            ClientListUpdated?.Invoke(msg.Clients);
                        }
                        break;
                    case SRSMessageType.VersionMismatch:
                        Console.WriteLine($"SRS version mismatch. Client={CLIENT_VERSION}, Server={msg.Version}");
                        break;
                }
            }
        }
        catch (SocketException)
        {
            Disconnect();
            _state.ServerStatus = ConnectionStatus.Error;
        }
        catch (IOException)
        {
            Disconnect();
            _state.ServerStatus = ConnectionStatus.Error;
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            if (_state.ServerStatus != ConnectionStatus.Error)
            {
                _state.ServerStatus = ConnectionStatus.Disconnected;
            }
        }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                if (ct.IsCancellationRequested) break;
                await SendMessageAsync(new SRSMessage { MsgType = SRSMessageType.PingBack });
                await SendUpdateAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateStateFromSettings(Dictionary<string, string> settings)
    {
        foreach (var kvp in settings)
        {
            var key = kvp.Key.Trim();
            var value = kvp.Value;
            switch (key.ToLowerInvariant())
            {
                case "overlayopacity":
                    if (double.TryParse(value, out var overlayOpacity)) _state.OverlayOpacity = overlayOpacity;
                    break;
                case "inputdevice":
                    _state.InputDeviceName = value;
                    break;
                case "outputdevice":
                    _state.OutputDeviceName = value;
                    break;
                case "micvolume":
                    if (float.TryParse(value, out var micVolume)) _state.MicVolume = micVolume;
                    break;
                case "speakervolume":
                    if (float.TryParse(value, out var speakerVolume)) _state.SpeakerVolume = speakerVolume;
                    break;
                case "radioeffectsenabled":
                    if (bool.TryParse(value, out var radioFx)) _state.RadioEffectsEnabled = radioFx;
                    break;
                case "commsoverlayvisible":
                    if (bool.TryParse(value, out var commsVisible)) _state.CommsOverlayVisible = commsVisible;
                    break;
                case "commandcentervisible":
                    if (bool.TryParse(value, out var cmdVisible)) _state.CommandCenterVisible = cmdVisible;
                    break;
                case "connectedclients":
                case "clients":
                    if (int.TryParse(value, out var clientCount)) _state.ConnectedClientCount = clientCount;
                    break;
            }
        }
    }

    private void UpdateTunedClientCounts(List<SRSClient> clients)
    {
        foreach (var slot in _state.Radios)
        {
            slot.TunedClientCount = 0;
        }

        foreach (var client in clients)
        {
            var radios = client.RadioInfo?.Radios;
            if (radios == null) continue;

            for (var i = 1; i < radios.Count && i < _state.Radios.Length; i++)
            {
                var radio = radios[i];
                if (radio.Modulation != 0) continue;
                _state.Radios[i].TunedClientCount++;
            }
        }
    }

    public void Dispose() { Disconnect(); _tcp?.Dispose(); }
}
