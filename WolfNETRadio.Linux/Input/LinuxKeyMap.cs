using System.Collections.Generic;
using WpfKey = Avalonia.Input.Key;

namespace WolfNETRadio.Input;

/// <summary>
/// Maps WPF/Avalonia Key enum values to Linux evdev key codes (input-event-codes.h).
/// Not exhaustive — covers the keys most useful for PTT bindings.
/// </summary>
public static class LinuxKeyMap
{
    // evdev key codes — matches /usr/include/linux/input-event-codes.h
    private static readonly Dictionary<WpfKey, int> _map = new()
    {
        // Alpha
        { WpfKey.A, 30 }, { WpfKey.B, 48 }, { WpfKey.C, 46 }, { WpfKey.D, 32 },
        { WpfKey.E, 18 }, { WpfKey.F, 33 }, { WpfKey.G, 34 }, { WpfKey.H, 35 },
        { WpfKey.I, 23 }, { WpfKey.J, 36 }, { WpfKey.K, 37 }, { WpfKey.L, 38 },
        { WpfKey.M, 50 }, { WpfKey.N, 49 }, { WpfKey.O, 24 }, { WpfKey.P, 25 },
        { WpfKey.Q, 16 }, { WpfKey.R, 19 }, { WpfKey.S, 31 }, { WpfKey.T, 20 },
        { WpfKey.U, 22 }, { WpfKey.V, 47 }, { WpfKey.W, 17 }, { WpfKey.X, 45 },
        { WpfKey.Y, 21 }, { WpfKey.Z, 44 },
        // F-keys
        { WpfKey.F1,  59 }, { WpfKey.F2,  60 }, { WpfKey.F3,  61 }, { WpfKey.F4,  62 },
        { WpfKey.F5,  63 }, { WpfKey.F6,  64 }, { WpfKey.F7,  65 }, { WpfKey.F8,  66 },
        { WpfKey.F9,  67 }, { WpfKey.F10, 68 }, { WpfKey.F11, 87 }, { WpfKey.F12, 88 },
        // Modifiers
        { WpfKey.LeftCtrl,   29  }, { WpfKey.RightCtrl,  97  },
        { WpfKey.LeftShift,  42  }, { WpfKey.RightShift, 54  },
        { WpfKey.LeftAlt,    56  }, { WpfKey.RightAlt,   100 },
        { WpfKey.LWin,       125 }, { WpfKey.RWin,       126 },
        // Special
        { WpfKey.Space,      57  }, { WpfKey.Return,     28  }, { WpfKey.Back, 14 },
        { WpfKey.Tab,        15  }, { WpfKey.Escape,     1   }, { WpfKey.Delete, 111 },
        { WpfKey.Insert,     110 }, { WpfKey.Home,       102 }, { WpfKey.End,  107 },
        { WpfKey.PageUp,     104 }, { WpfKey.PageDown,   109 },
        { WpfKey.Up,         103 }, { WpfKey.Down,       108 },
        { WpfKey.Left,       105 }, { WpfKey.Right,      106 },
        // Numpad
        { WpfKey.NumPad0, 82 }, { WpfKey.NumPad1, 79 }, { WpfKey.NumPad2, 80 },
        { WpfKey.NumPad3, 81 }, { WpfKey.NumPad4, 75 }, { WpfKey.NumPad5, 76 },
        { WpfKey.NumPad6, 77 }, { WpfKey.NumPad7, 71 }, { WpfKey.NumPad8, 72 },
        { WpfKey.NumPad9, 73 }, { WpfKey.Divide, 98 }, { WpfKey.Multiply, 55 },
        { WpfKey.Subtract, 74 }, { WpfKey.Add, 78 }, { WpfKey.Decimal, 83 },
        // CapsLock, etc.
        { WpfKey.CapsLock, 58 }, { WpfKey.Scroll, 70 }, { WpfKey.NumLock, 69 },
    };

    /// <summary>Returns the evdev keycode for a WpfKey, or -1 if not mapped.</summary>
    public static int ToEvdev(WpfKey? key)
    {
        if (key == null) return -1;
        return _map.TryGetValue(key.Value, out var code) ? code : -1;
    }
}
