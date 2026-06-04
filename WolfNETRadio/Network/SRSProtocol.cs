using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using WolfNETRadio.Models;

namespace WolfNETRadio.Network;

// ── Message type discriminators ──────────────────────────────────────────────
public enum SRSMessageType
{
    Update = 0,
    PingBack = 1,
    Sync = 2,
    RadioUpdate = 3,
    ServerSettings = 4,
    ClientDisconnect = 5,
    VersionMismatch = 6,
    ExternalAwacsMode = 7,
    ExternalAwacsModeDisconnect = 8,
}

// ── Wire models ───────────────────────────────────────────────────────────────

public class SRSMessage
{
    [JsonPropertyName("MsgType")] public SRSMessageType MsgType { get; set; }
    [JsonPropertyName("Client")]  public SRSClient? Client { get; set; }
    [JsonPropertyName("Clients")] public List<SRSClient>? Clients { get; set; }
    [JsonPropertyName("ServerSettings")] public Dictionary<string, string>? ServerSettings { get; set; }
    [JsonPropertyName("ExternalAWACSModePassword")] public string? RadioAccessKey { get; set; }
    [JsonPropertyName("Version")] public string? Version { get; set; }
}

public class SRSClient
{
    [JsonPropertyName("ClientGuid")] public string ClientGuid { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("Name")]       public string Name { get; set; } = "UNKNOWN";
    [JsonPropertyName("Coalition")]  public int Coalition { get; set; } = 1;
    [JsonPropertyName("RadioInfo")]  public SRSRadioInfo? RadioInfo { get; set; }
    [JsonPropertyName("Position")]   public SRSPosition Position { get; set; } = new();
    [JsonPropertyName("Seat")]       public int Seat { get; set; }
}

public class SRSRadioInfo
{
    [JsonPropertyName("radios")] public List<SRSRadio> Radios { get; set; } = [];
    [JsonPropertyName("unit")]   public string Unit { get; set; } = "WolfNET";
    [JsonPropertyName("unitId")] public uint UnitId { get; set; } = 1;
    [JsonPropertyName("inAircraft")] public bool InAircraft { get; set; } = false;
    [JsonPropertyName("name")]   public string Name { get; set; } = "WolfNET Radio";

    public static SRSRadioInfo FromClientState(ClientState state)
    {
        var info = new SRSRadioInfo { Name = state.Callsign };
        // Slot 0 = "empty" sentinel required by SRS protocol
        info.Radios.Add(new SRSRadio { Modulation = 4 }); // DISABLED
        foreach (var slot in state.Radios.Skip(1).Take(10))
        {
            info.Radios.Add(new SRSRadio
            {
                Freq = slot.FrequencyHz,
                Modulation = 0, // CHANNEL
                Retransmit = slot.IsRetransmit,
                Volume = slot.Volume,
            });
        }
        return info;
    }
}

public class SRSRadio
{
    [JsonPropertyName("freq")]       public double Freq { get; set; } = 1_001_000.0;
    [JsonPropertyName("modulation")] public int Modulation { get; set; } = 0; // 0=CHANNEL
    [JsonPropertyName("retransmit")] public bool Retransmit { get; set; }
    [JsonPropertyName("volume")]     public float Volume { get; set; } = 1.0f;
    [JsonPropertyName("freqMin")]    public double FreqMin { get; set; } = 1_000_000.0;
    [JsonPropertyName("freqMax")]    public double FreqMax { get; set; } = 10_999_000.0;
    [JsonPropertyName("freqMode")]   public int FreqMode { get; set; } = 0;
    [JsonPropertyName("volMode")]    public int VolMode { get; set; } = 0;
    [JsonPropertyName("secFreq")]    public double SecFreq { get; set; } = 0.0;
    [JsonPropertyName("channel")]    public int Channel { get; set; } = -1;
    [JsonPropertyName("enc")]        public bool Enc { get; set; } = false;
    [JsonPropertyName("encKey")]     public byte EncKey { get; set; } = 0;
    [JsonPropertyName("encMode")]    public int EncMode { get; set; } = 0;
    [JsonPropertyName("expansion")]  public bool Expansion { get; set; } = false;
}

public class SRSPosition
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("z")] public double Z { get; set; }
}

// ── UDP voice packet ──────────────────────────────────────────────────────────

public class SRSVoicePacket
{
    public byte[] Audio { get; set; } = [];          // Opus-encoded frame
    public double[] Frequencies { get; set; } = [];  // transmission freqs
    public int[] Modulations { get; set; } = [];
    public int[] Encryptions { get; set; } = [];
    public string ClientGuid { get; set; } = string.Empty;
    public string OriginalClientGuid { get; set; } = string.Empty;
    public ulong PacketId { get; set; }
    public byte RetransmissionCount { get; set; }
    public bool Retransmission { get; set; }

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

// ── Serialization helpers ─────────────────────────────────────────────────────

public static class SRSSerializer
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(SRSMessage msg) =>
        JsonSerializer.Serialize(msg, Opts) + "\n";

    public static SRSMessage? Deserialize(string json) =>
        JsonSerializer.Deserialize<SRSMessage>(json, Opts);
}
