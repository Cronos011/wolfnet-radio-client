using Concentus;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Concentus.Enums;
using Concentus.Structs;
using WolfNETRadio.Models;

namespace WolfNETRadio.Network;

public class SRSVoiceClient : IDisposable
{
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private readonly ClientState _state;
    private string _clientGuid = string.Empty;
    private ulong _packetId;

    // Events
    public event Action<float[], string>? AudioReceived; // (pcmSamples, fromGuid)

    private OpusEncoder? _encoder;
    private OpusDecoder? _decoder;

    public const int SAMPLE_RATE = 16000;
    public const int CHANNELS = 1;
    public const int FRAME_SIZE = 960; // 60ms at 16kHz

    public SRSVoiceClient(ClientState state) => _state = state;

    public void Connect(string host, int port)
    {
        _state.VoipStatus = ConnectionStatus.Connecting;
        _udp = new UdpClient();
        _udp.Connect(host, port);

        _encoder = OpusEncoder.Create(SAMPLE_RATE, CHANNELS, OpusApplication.OPUS_APPLICATION_VOIP);
        _decoder = OpusDecoder.Create(SAMPLE_RATE, CHANNELS);

        if (string.IsNullOrWhiteSpace(_clientGuid))
        {
            _clientGuid = Guid.NewGuid().ToString();
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _state.VoipStatus = ConnectionStatus.Connected;
    }

    public void SendAudio(float[] pcmSamples, int[] activeRadioIds)
    {
        if (_udp == null || _encoder == null) return;
        if (activeRadioIds.Length == 0) return;

        var freqs = new List<double>();
        var mods = new List<int>();
        var encs = new List<int>();

        foreach (var id in activeRadioIds)
        {
            if (id < 0 || id >= _state.Radios.Length) continue;
            var slot = _state.Radios[id];
            freqs.Add(slot.FrequencyHz);
            mods.Add(0);
            encs.Add(0);
        }

        if (freqs.Count == 0) return;

        var pcmFrame = new float[FRAME_SIZE];
        var copyLen = Math.Min(FRAME_SIZE, pcmSamples.Length);
        Array.Copy(pcmSamples, 0, pcmFrame, 0, copyLen);

        var opusBuffer = new byte[4000];
        var opusLen = _encoder.Encode(pcmFrame, 0, FRAME_SIZE, opusBuffer, 0, opusBuffer.Length);
        if (opusLen <= 0) return;

        var meta = new Dictionary<string, object>
        {
            ["Frequencies"] = freqs.ToArray(),
            ["Modulations"] = mods.ToArray(),
            ["Encryptions"] = encs.ToArray(),
            ["UnitId"] = 1,
            ["RetransmissionCount"] = 0,
            ["OriginalClientGuid"] = _clientGuid,
            ["ClientGuid"] = _clientGuid,
        };

        var metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { PropertyNamingPolicy = null });
        var metaBytes = Encoding.UTF8.GetBytes(metaJson);

        var guidBytes = new byte[16];
        var guidUtf8 = Encoding.UTF8.GetBytes(_clientGuid);
        Buffer.BlockCopy(guidUtf8, 0, guidBytes, 0, Math.Min(16, guidUtf8.Length));

        var packetIdBytes = BitConverter.GetBytes(_packetId++);
        if (!BitConverter.IsLittleEndian) Array.Reverse(packetIdBytes);

        var packet = new byte[16 + 8 + opusLen + metaBytes.Length];
        Buffer.BlockCopy(guidBytes, 0, packet, 0, 16);
        Buffer.BlockCopy(packetIdBytes, 0, packet, 16, 8);
        Buffer.BlockCopy(opusBuffer, 0, packet, 24, opusLen);
        Buffer.BlockCopy(metaBytes, 0, packet, 24 + opusLen, metaBytes.Length);

        _udp.Send(packet, packet.Length);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (_udp == null || _decoder == null) return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _udp.ReceiveAsync(ct);
                var buffer = result.Buffer;
                if (buffer.Length < 24) continue;

                var guidBytes = new byte[16];
                Buffer.BlockCopy(buffer, 0, guidBytes, 0, 16);
                var headerGuid = Encoding.UTF8.GetString(guidBytes).TrimEnd('\0');

                var packetIdBytes = new byte[8];
                Buffer.BlockCopy(buffer, 16, packetIdBytes, 0, 8);
                if (!BitConverter.IsLittleEndian) Array.Reverse(packetIdBytes);
                var packetId = BitConverter.ToUInt64(packetIdBytes, 0);
                _ = packetId;

                var jsonStart = Array.LastIndexOf(buffer, (byte)'{');
                if (jsonStart < 24) continue;

                var opusLen = jsonStart - 24;
                if (opusLen <= 0) continue;

                var opusData = new byte[opusLen];
                Buffer.BlockCopy(buffer, 24, opusData, 0, opusLen);

                var metaJson = Encoding.UTF8.GetString(buffer, jsonStart, buffer.Length - jsonStart);
                string fromGuid = headerGuid;
                try
                {
                    using var doc = JsonDocument.Parse(metaJson);
                    if (doc.RootElement.TryGetProperty("ClientGuid", out var cg))
                    {
                        fromGuid = cg.GetString() ?? fromGuid;
                    }
                    else if (doc.RootElement.TryGetProperty("clientGuid", out var cg2))
                    {
                        fromGuid = cg2.GetString() ?? fromGuid;
                    }
                }
                catch (JsonException)
                {
                }

                var pcm = new float[FRAME_SIZE];
                var samples = _decoder.Decode(opusData, 0, opusData.Length, pcm, 0, FRAME_SIZE, false);
                if (samples <= 0) continue;

                if (samples != pcm.Length)
                {
                    var trimmed = new float[samples];
                    Array.Copy(pcm, trimmed, samples);
                    pcm = trimmed;
                }

                AudioReceived?.Invoke(pcm, fromGuid);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
            Disconnect();
            _state.VoipStatus = ConnectionStatus.Error;
        }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _udp?.Close();
        _state.VoipStatus = ConnectionStatus.Disconnected;
    }

    public void Dispose() { Disconnect(); }
}
