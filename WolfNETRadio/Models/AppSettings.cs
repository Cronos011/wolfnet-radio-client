using System.IO;
using System.Text.Json;

namespace WolfNETRadio.Models;

/// <summary>
/// Persisted user settings — saved to %APPDATA%\WolfNETRadio\settings.json on exit,
/// loaded on startup.
/// </summary>
public class AppSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfNETRadio");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    // ── Persisted fields ──────────────────────────────────────────────
    public string RadioAccessKey   { get; set; } = string.Empty;
    public string ServerAddress    { get; set; } = "gwrecon.com:5002";
    public string InputDeviceName  { get; set; } = string.Empty;
    public string OutputDeviceName { get; set; } = string.Empty;
    public float  MicVolume        { get; set; } = 1.0f;
    public float  SpeakerVolume    { get; set; } = 1.0f;
    public bool   RadioEffects     { get; set; } = true;
    public double OverlayOpacity   { get; set; } = 1.0;
    public float  VoxThreshold     { get; set; } = 0.05f;
    public int    VoxHangtimeMs    { get; set; } = 300;

    // ── Load / Save ───────────────────────────────────────────────────

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* first run or corrupt file — fall through */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Apply loaded settings into ClientState so the rest of the app
    /// sees the restored values on startup.
    /// </summary>
    public void ApplyTo(ClientState state)
    {
        if (!string.IsNullOrWhiteSpace(RadioAccessKey)) state.RadioAccessKey   = RadioAccessKey;
        if (!string.IsNullOrWhiteSpace(ServerAddress))  state.ServerAddress    = ServerAddress;
        if (!string.IsNullOrWhiteSpace(InputDeviceName))  state.InputDeviceName  = InputDeviceName;
        if (!string.IsNullOrWhiteSpace(OutputDeviceName)) state.OutputDeviceName = OutputDeviceName;
        state.MicVolume           = MicVolume;
        state.SpeakerVolume       = SpeakerVolume;
        state.RadioEffectsEnabled = RadioEffects;
        state.OverlayOpacity      = OverlayOpacity;
        state.VoxThreshold        = VoxThreshold;
        state.VoxHangtimeMs       = VoxHangtimeMs;
    }

    /// <summary>
    /// Snapshot current ClientState into this settings object ready for Save().
    /// </summary>
    public void SnapshotFrom(ClientState state)
    {
        RadioAccessKey   = state.RadioAccessKey;
        ServerAddress    = state.ServerAddress;
        InputDeviceName  = state.InputDeviceName;
        OutputDeviceName = state.OutputDeviceName;
        MicVolume        = state.MicVolume;
        SpeakerVolume    = state.SpeakerVolume;
        RadioEffects     = state.RadioEffectsEnabled;
        OverlayOpacity   = state.OverlayOpacity;
        VoxThreshold     = state.VoxThreshold;
        VoxHangtimeMs    = state.VoxHangtimeMs;
    }
}
