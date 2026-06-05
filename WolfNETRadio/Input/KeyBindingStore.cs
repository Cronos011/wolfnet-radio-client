using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using WpfKey = System.Windows.Input.Key;

namespace WolfNETRadio.Input;

// ── Input device types ───────────────────────────────────────────────────────

public enum InputDeviceType { None, Keyboard, Mouse, Joystick }

/// <summary>
/// Unified input binding that can represent a keyboard key, mouse button,
/// or joystick button on a specific device.
/// </summary>
public class InputTrigger
{
    public InputDeviceType DeviceType { get; set; } = InputDeviceType.None;

    // Keyboard
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WpfKey? KeyboardKey { get; set; }

    // Mouse — VK virtual key code (e.g. VK_LBUTTON=1, RBUTTON=2, MBUTTON=4, XBUTTON1=5, XBUTTON2=6)
    public int MouseVk { get; set; }

    // Joystick — WinMM device id (0-15) + button index (0-31)
    public int JoystickId   { get; set; }
    public string JoystickName { get; set; } = string.Empty;
    public int JoystickButton { get; set; }

    [JsonIgnore]
    public string Display => DeviceType switch
    {
        InputDeviceType.Keyboard => KeyboardKey?.ToString() ?? "[UNBOUND]",
        InputDeviceType.Mouse    => MouseVkToName(MouseVk),
        InputDeviceType.Joystick => $"{TruncName(JoystickName.Length > 0 ? JoystickName : $"Joy{JoystickId}")} B{JoystickButton + 1}",
        _                        => "[UNBOUND]"
    };

    private static string MouseVkToName(int vk) => vk switch
    {
        1 => "Mouse LBtn",  2 => "Mouse RBtn",  4 => "Mouse MBtn",
        5 => "Mouse X1",    6 => "Mouse X2",
        _ => $"Mouse VK{vk}"
    };

    private static string TruncName(string n) =>
        n.Length > 14 ? n[..14] + "…" : n;
}

// ── Per-radio binding ────────────────────────────────────────────────────────

public class PttBinding
{
    public int RadioId { get; set; }
    public string RadioLabel { get; set; } = string.Empty;
    public InputTrigger? Primary { get; set; }
    public InputTrigger? Modifier { get; set; }

    // Legacy display helpers for UI
    [JsonIgnore] public string PrimaryKeyDisplay  => Primary?.Display  ?? "[UNBOUND]";
    [JsonIgnore] public string ModifierKeyDisplay => Modifier?.Display ?? "None";

    // Legacy keyboard-only helpers so existing code that checks Key? keeps working
    [JsonIgnore] public WpfKey? PrimaryKey  => Primary?.DeviceType  == InputDeviceType.Keyboard ? Primary.KeyboardKey   : null;
    [JsonIgnore] public WpfKey? ModifierKey => Modifier?.DeviceType == InputDeviceType.Keyboard ? Modifier.KeyboardKey  : null;
}

// ── Binding store ────────────────────────────────────────────────────────────

public class KeyBindingStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WolfNET Radio", "keybindings.json");

    private List<PttBinding> _bindings = [];

    public IReadOnlyList<PttBinding> Bindings => _bindings;

    public KeyBindingStore()
    {
        _bindings = BuildDefaults();
        Load();
    }

    private static List<PttBinding> BuildDefaults()
    {
        var list = new List<PttBinding>();
        for (int i = 1; i <= 3; i++)
            list.Add(new PttBinding { RadioId = i, RadioLabel = $"Radio {i} (CH-{i})" });
        list.Add(new PttBinding { RadioId = 0,  RadioLabel = "Intercom" });
        list.Add(new PttBinding { RadioId = -1, RadioLabel = "Common PTT (all active)" });
        return list;
    }

    public void SetPrimary(int radioId, InputTrigger trigger)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.Primary = trigger; Save(); }
    }

    public void SetModifier(int radioId, InputTrigger trigger)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.Modifier = trigger; Save(); }
    }

    // Legacy keyboard-only setters (used by old capture code)
    public void SetPrimary(int radioId, WpfKey key)  => SetPrimary(radioId,  new InputTrigger { DeviceType = InputDeviceType.Keyboard, KeyboardKey = key });
    public void SetModifier(int radioId, WpfKey key) => SetModifier(radioId, new InputTrigger { DeviceType = InputDeviceType.Keyboard, KeyboardKey = key });

    public void ClearPrimary(int radioId)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.Primary = null; Save(); }
    }

    public void ClearModifier(int radioId)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.Modifier = null; Save(); }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var opts = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            var saved = JsonSerializer.Deserialize<List<PttBinding>>(File.ReadAllText(SettingsPath), opts);
            if (saved == null) return;
            foreach (var s in saved)
            {
                var b = _bindings.FirstOrDefault(x => x.RadioId == s.RadioId);
                if (b != null) { b.Primary = s.Primary; b.Modifier = s.Modifier; }
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var opts = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_bindings, opts));
        }
        catch { }
    }
}
