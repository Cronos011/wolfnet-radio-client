using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace WolfNETRadio.Network;

public record AuthResult(bool Valid, string Callsign, int Rank, string? Error);

public class GwReconAuthClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string VALIDATE_URL = "https://gwrecon.com/comms/api/radio/validate-key";

    public async Task<AuthResult> ValidateKeyAsync(string radioAccessKey)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, VALIDATE_URL);
            request.Headers.Add("X-Radio-Key", radioAccessKey);

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return new AuthResult(false, string.Empty, 0, $"HTTP {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var valid = root.TryGetProperty("valid", out var v) && v.GetBoolean();
            var callsign = root.TryGetProperty("callsign", out var c) ? (c.GetString() ?? string.Empty) : string.Empty;
            var rank = root.TryGetProperty("rank", out var r) ? r.GetInt32() : 0;

            return new AuthResult(valid, callsign, rank, valid ? null : "Invalid");
        }
        catch (Exception ex)
        {
            return new AuthResult(false, string.Empty, 0, ex.Message);
        }
    }
    public async Task<List<MissionPreset>> GetMissionsAsync(string radioAccessKey)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://gwrecon.com/comms/api/radio/my-missions");
            request.Headers.Add("X-Radio-Key", radioAccessKey);
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return [];

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var missions = new List<MissionPreset>();
            if (!doc.RootElement.TryGetProperty("missions", out var arr)) return missions;

            foreach (var m in arr.EnumerateArray())
            {
                var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var channels = new List<ChannelPreset>();
                if (m.TryGetProperty("channels", out var ch))
                {
                    foreach (var c2 in ch.EnumerateArray())
                    {
                        var code = c2.TryGetProperty("code", out var co) ? co.GetInt32() : 0;
                        var label = c2.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                        var slot = c2.TryGetProperty("slot", out var s) ? s.GetInt32() : 1;
                        channels.Add(new ChannelPreset(code, label, slot));
                    }
                }
                // Intercom (user's own ship, slot 0)
                ChannelPreset? intercom = null;
                if (m.TryGetProperty("intercom", out var ic) && ic.ValueKind == JsonValueKind.Object)
                {
                    var iCode  = ic.TryGetProperty("code",  out var iC) ? iC.GetInt32() : 0;
                    var iLabel = ic.TryGetProperty("label", out var iL) ? iL.GetString() ?? "" : "";
                    intercom = new ChannelPreset(iCode, iLabel, 0);
                }

                missions.Add(new MissionPreset(name, channels, intercom));
            }
            return missions;
        }
        catch { return []; }
    }

}

// ── Server preset fetching ────────────────────────────────────────────────────

public record MissionPreset(string MissionName, List<ChannelPreset> Channels, ChannelPreset? Intercom);
public record ChannelPreset(int Code, string Label, int Slot);

