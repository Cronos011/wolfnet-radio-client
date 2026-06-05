using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using WpfKey = Avalonia.Input.Key;

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

    // Primary PTT (with optional modifier)
    public InputTrigger? Primary { get; set; }
    public InputTrigger? PrimaryModifier { get; set; }

    // Secondary PTT (independent — either fires the radio, with optional modifier)
    public InputTrigger? Secondary { get; set; }
    public InputTrigger? SecondaryModifier { get; set; }

    // Legacy display helpers for UI
    [JsonIgnore] public string PrimaryDisplay => Primary?.Display ?? "[UNBOUND]";
    [JsonIgnore] public string PrimaryModDisplay => PrimaryModifier?.Display ?? "None";
    [JsonIgnore] public string SecondaryDisplay => Secondary?.Display ?? "[UNBOUND]";
    [JsonIgnore] public string SecondaryModDisplay => SecondaryModifier?.Display ?? "None";

    // Legacy keyboard-only helpers so existing code that checks Key? keeps working
    [JsonIgnore] public WpfKey? PrimaryKey => Primary?.DeviceType == InputDeviceType.Keyboard ? Primary.KeyboardKey : null;
    [JsonIgnore] public WpfKey? ModifierKey => PrimaryModifier?.DeviceType == InputDeviceType.Keyboard ? PrimaryModifier.KeyboardKey : null;
}

// ── Channel switch binding ────────────────────────────────────────────────────────

public class ChannelSwitchBinding
{
    public int RadioId { get; set; }        // 1-10
    public string RadioLabel { get; set; } = string.Empty;
    public InputTrigger? SwitchTrigger { get; set; }
    [JsonIgnore] public string SwitchDisplay => SwitchTrigger?.Display ?? "[UNBOUND]";
}

// ── Binding store ────────────────────────────────────────────────────────────

public class KeyBindingStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WolfNET Radio", "keybindings.json");

    private List<PttBinding> _bindings = [];
    private List<ChannelSwitchBinding> _switchBindings = [];

    public IReadOnlyList<PttBinding> Bindings => _bindings;
    public IReadOnlyList<ChannelSwitchBinding> SwitchBindings => _switchBindings;

    public KeyBindingStore()
    {
        _bindings = BuildDefaults();
        _switchBindings = BuildSwitchDefaults();
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

    private static List<ChannelSwitchBinding> BuildSwitchDefaults() =>
        Enumerable.Range(1, 10)
            .Select(i => new ChannelSwitchBinding { RadioId = i, RadioLabel = $"RADIO {i} A↔B" })
            .ToList();

    public void SetPrimary(int radioId, InputTrigger trigger)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.Primary = trigger; Save(); }
    }

    public void SetPrimaryModifier(int radioId, InputTrigger trigger)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.PrimaryModifier = trigger; Save(); }
    }

    public void SetSecondary(int radioId, InputTrigger trigger)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.Secondary = trigger; Save(); }
    }

    public void SetSecondaryModifier(int radioId, InputTrigger trigger)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.SecondaryModifier = trigger; Save(); }
    }

    // Legacy keyboard-only setters (used by old capture code)
    public void SetPrimary(int radioId, WpfKey key)  => SetPrimary(radioId,  new InputTrigger { DeviceType = InputDeviceType.Keyboard, KeyboardKey = key });

    public void SetModifier(int radioId, InputTrigger trigger) => SetPrimaryModifier(radioId, trigger);
    public void SetModifier(int radioId, WpfKey key) => SetPrimaryModifier(radioId, new InputTrigger { DeviceType = InputDeviceType.Keyboard, KeyboardKey = key });

    public void ClearPrimary(int radioId)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.Primary = null; Save(); }
    }

    public void ClearPrimaryModifier(int radioId)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.PrimaryModifier = null; Save(); }
    }

    public void ClearSecondary(int radioId)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.Secondary = null; Save(); }
    }

    public void ClearSecondaryModifier(int radioId)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.SecondaryModifier = null; Save(); }
    }

    public void ClearModifier(int radioId) => ClearPrimaryModifier(radioId);

    // Channel switch bindings
    public void SetSwitchTrigger(int radioId, InputTrigger trigger)
    {
        var b = _switchBindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.SwitchTrigger = trigger; Save(); }
    }

    public void ClearSwitchTrigger(int radioId)
    {
        var b = _switchBindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null) { b.SwitchTrigger = null; Save(); }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var opts = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            var json = File.ReadAllText(SettingsPath);
            
            // Try loading as wrapper with both PTT and switch bindings
            var wrapper = JsonSerializer.Deserialize<BindingWrapper>(json, opts);
            if (wrapper != null)
            {
                if (wrapper.PttBindings != null)
                {
                    foreach (var s in wrapper.PttBindings)
                    {
                        var b = _bindings.FirstOrDefault(x => x.RadioId == s.RadioId);
                        if (b != null)
                        {
                            b.Primary = s.Primary;
                            b.PrimaryModifier = s.PrimaryModifier;
                            b.Secondary = s.Secondary;
                            b.SecondaryModifier = s.SecondaryModifier;
                        }
                    }
                }
                if (wrapper.SwitchBindings != null)
                {
                    foreach (var s in wrapper.SwitchBindings)
                    {
                        var b = _switchBindings.FirstOrDefault(x => x.RadioId == s.RadioId);
                        if (b != null) { b.SwitchTrigger = s.SwitchTrigger; }
                    }
                }
            }
            else
            {
                // Fall back to loading as List<PttBinding> for backward compat
                var saved = JsonSerializer.Deserialize<List<PttBinding>>(json, opts);
                if (saved != null)
                {
                    foreach (var s in saved)
                    {
                        var b = _bindings.FirstOrDefault(x => x.RadioId == s.RadioId);
                        if (b != null) { b.Primary = s.Primary; b.PrimaryModifier = s.PrimaryModifier; }
                    }
                }
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
            var wrapper = new BindingWrapper { PttBindings = _bindings, SwitchBindings = _switchBindings };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(wrapper, opts));
        }
        catch { }
    }

    // ── Serialization wrapper ────────────────────────────────────────────
    private class BindingWrapper
    {
        public List<PttBinding>? PttBindings { get; set; }
        public List<ChannelSwitchBinding>? SwitchBindings { get; set; }
    }
}
