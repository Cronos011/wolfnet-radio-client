using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;

namespace WolfNETRadio.Input;

public class PttBinding
{
    public int RadioId { get; set; }
    public string RadioLabel { get; set; } = string.Empty;
    public Key? PrimaryKey { get; set; }
    public Key? ModifierKey { get; set; }

    public string PrimaryKeyDisplay => PrimaryKey.HasValue ? PrimaryKey.Value.ToString() : "[UNBOUND]";
    public string ModifierKeyDisplay => ModifierKey.HasValue ? ModifierKey.Value.ToString() : "None";
}

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
        list.Add(new PttBinding { RadioId = 0, RadioLabel = "Intercom" });
        list.Add(new PttBinding { RadioId = -1, RadioLabel = "Common PTT (all active)" });
        return list;
    }

    public void SetPrimary(int radioId, Key key)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null)
        {
            b.PrimaryKey = key;
            Save();
        }
    }

    public void SetModifier(int radioId, Key key)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null)
        {
            b.ModifierKey = key;
            Save();
        }
    }

    public void ClearPrimary(int radioId)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null)
        {
            b.PrimaryKey = null;
            Save();
        }
    }

    public void ClearModifier(int radioId)
    {
        var b = _bindings.FirstOrDefault(x => x.RadioId == radioId);
        if (b != null)
        {
            b.ModifierKey = null;
            Save();
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var saved = JsonSerializer.Deserialize<List<PttBinding>>(File.ReadAllText(SettingsPath));
            if (saved == null) return;
            foreach (var s in saved)
            {
                var b = _bindings.FirstOrDefault(x => x.RadioId == s.RadioId);
                if (b != null)
                {
                    b.PrimaryKey = s.PrimaryKey;
                    b.ModifierKey = s.ModifierKey;
                }
            }
        }
        catch
        {
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_bindings));
        }
        catch
        {
        }
    }
}
