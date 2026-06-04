using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace WolfNETRadio.Network;

public record AuthResult(bool Valid, string Callsign, int Rank, string? Error);

public class GwReconAuthClient
{
    private readonly HttpClient _http = new();
    private const string VALIDATE_URL = "https://gwrecon.com/comms/api/radio/validate-key";

    public async Task<AuthResult> ValidateKeyAsync(string radioAccessKey)
    {
        try
        {
            _http.Timeout = TimeSpan.FromSeconds(10);
            using var request = new HttpRequestMessage(HttpMethod.Post, VALIDATE_URL);
            request.Headers.Add("X-Radio-Key", radioAccessKey);
            var payload = JsonSerializer.Serialize(new { key = radioAccessKey });
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

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
}
