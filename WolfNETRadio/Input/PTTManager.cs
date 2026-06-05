using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using WpfKey = System.Windows.Input.Key;
using Vortice.DirectInput;

namespace WolfNETRadio.Input;

/// <summary>
/// Manages PTT (Push-To-Talk) bindings across keyboard, mouse, and joystick/HOTAS devices.
/// Uses low-level Win32 hooks for keyboard + mouse (works even when app is unfocused).
/// Joystick/HID devices are polled on a background thread via DirectInput.
/// </summary>
public class PTTManager : IDisposable
{
    // radioId → binding pair
    private List<PttBinding> _bindings = [];

    private readonly HashSet<int> _activeRadios = [];
    public event Action<int, bool>? PTTStateChanged; // (radioId, isPressed)

    // ── Keyboard hook ────────────────────────────────────────────────────────
    private nint _kbHook = nint.Zero;
    private Win32.LowLevelKeyboardProc? _kbProc;
    // alias to avoid ambiguity with Vortice.DirectInput.Key
    private static WpfKey KeyFromVk(int vk) => System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk);

    // ── Mouse hook ───────────────────────────────────────────────────────────
    private nint _mouseHook = nint.Zero;
    private Win32.LowLevelMouseProc? _mouseProc;

    // ── Joystick polling ─────────────────────────────────────────────────────
    private IDirectInput8? _di;
    private readonly List<(IDirectInputDevice8 Device, Guid Guid, string Name)> _joysticks = [];
    private Thread? _pollThread;
    private volatile bool _pollRunning;
    private readonly Dictionary<(Guid, int), bool> _joyBtnState = [];

    // ── Public API ───────────────────────────────────────────────────────────

    public void SetBindings(IReadOnlyList<PttBinding> bindings)
    {
        _bindings = bindings.ToList();
    }

    /// <summary>Returns a list of all connected joystick/HOTAS device names with their GUIDs.</summary>
    public List<(Guid Guid, string Name)> GetJoystickDevices()
    {
        var result = new List<(Guid, string)>();
        try
        {
            _di ??= DInput.DirectInput8Create();
            var infos = _di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
            foreach (var info in infos)
                result.Add((info.InstanceGuid, info.InstanceName.TrimEnd('\0')));
        }
        catch { }
        return result;
    }

    public void InstallHook()
    {
        InstallKeyboardHook();
        InstallMouseHook();
        StartJoystickPoll();
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    private void InstallKeyboardHook()
    {
        if (_kbHook != nint.Zero) return;
        _kbProc = KbHookCallback;
        using var proc = Process.GetCurrentProcess();
        _kbHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _kbProc,
            Win32.GetModuleHandle(proc.MainModule?.ModuleName), 0);
    }

    private nint KbHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var msg    = wParam.ToInt32();
            var isDown = msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN;
            var isUp   = msg == Win32.WM_KEYUP   || msg == Win32.WM_SYSKEYUP;

            if (isDown || isUp)
            {
                var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                var key  = KeyFromVk((int)info.vkCode);
                HandleTrigger(InputDeviceType.Keyboard, keyboardKey: key, isDown: isDown);
            }
        }
        return Win32.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    // ── Mouse ────────────────────────────────────────────────────────────────

    private void InstallMouseHook()
    {
        if (_mouseHook != nint.Zero) return;
        _mouseProc = MouseHookCallback;
        using var proc = Process.GetCurrentProcess();
        _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc,
            Win32.GetModuleHandle(proc.MainModule?.ModuleName), 0);
    }

    private nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            int? vk  = null;
            bool? dn = null;

            switch (msg)
            {
                case Win32.WM_LBUTTONDOWN: vk = 1; dn = true;  break;
                case Win32.WM_LBUTTONUP:   vk = 1; dn = false; break;
                case Win32.WM_RBUTTONDOWN: vk = 2; dn = true;  break;
                case Win32.WM_RBUTTONUP:   vk = 2; dn = false; break;
                case Win32.WM_MBUTTONDOWN: vk = 4; dn = true;  break;
                case Win32.WM_MBUTTONUP:   vk = 4; dn = false; break;
                case Win32.WM_XBUTTONDOWN:
                case Win32.WM_XBUTTONUP:
                    var ms = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                    var xb = (ms.mouseData >> 16) & 0xFFFF;
                    vk = xb == 1 ? 5 : 6;
                    dn = msg == Win32.WM_XBUTTONDOWN;
                    break;
            }

            if (vk.HasValue && dn.HasValue)
                HandleTrigger(InputDeviceType.Mouse, mouseVk: vk.Value, isDown: dn.Value);
        }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    // ── Joystick polling ─────────────────────────────────────────────────────

    private void StartJoystickPoll()
    {
        if (_pollRunning) return;
        _pollRunning = true;
        _pollThread = new Thread(JoystickPollLoop) { IsBackground = true, Name = "WolfNET-JoyPoll" };
        _pollThread.Start();
    }

    private void JoystickPollLoop()
    {
        try
        {
            _di ??= DInput.DirectInput8Create();
            var infos = _di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);

            foreach (var info in infos)
            {
                try
                {
                    var dev = _di.CreateDevice(info.InstanceGuid);
                    dev.SetDataFormat<RawJoystickState>();
                    dev.SetCooperativeLevel(nint.Zero,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    dev.Acquire();
                    _joysticks.Add((dev, info.InstanceGuid, info.InstanceName.TrimEnd('\0')));
                }
                catch { /* skip devices we can't acquire */ }
            }
        }
        catch { }

        while (_pollRunning)
        {
            foreach (var (dev, guid, _) in _joysticks)
            {
                try
                {
                    dev.Poll();
                    var state = dev.GetCurrentState<RawJoystickState>();

                    for (int btn = 0; btn < state.Buttons.Length; btn++)
                    {
                        var pressed = (state.Buttons[btn] & 0x80) != 0;
                        var key     = (guid, btn);
                        var wasDown = _joyBtnState.GetValueOrDefault(key, false);

                        if (pressed != wasDown)
                        {
                            _joyBtnState[key] = pressed;
                            HandleTrigger(InputDeviceType.Joystick,
                                joystickGuid: guid, joystickBtn: btn, isDown: pressed);
                        }
                    }
                }
                catch { }
            }

            Thread.Sleep(8); // ~120 Hz poll
        }
    }

    // ── Unified trigger matcher ───────────────────────────────────────────────

    private void HandleTrigger(
        InputDeviceType deviceType,
        WpfKey keyboardKey = WpfKey.None,
        int mouseVk = 0,
        Guid joystickGuid = default,
        int joystickBtn = 0,
        bool isDown = false)
    {
        foreach (var binding in _bindings)
        {
            // Check modifier first — if set, it must be held for primary to fire
            if (binding.Modifier != null)
            {
                bool modHeld = IsHeld(binding.Modifier);
                if (!modHeld) continue;
            }

            var trigger = binding.Primary;
            if (trigger == null) continue;

            bool matches = trigger.DeviceType == deviceType && deviceType switch
            {
                InputDeviceType.Keyboard => (trigger.KeyboardKey ?? WpfKey.None) == keyboardKey,
                InputDeviceType.Mouse    => trigger.MouseVk      == mouseVk,
                InputDeviceType.Joystick => trigger.JoystickGuid == joystickGuid
                                         && trigger.JoystickButton == joystickBtn,
                _ => false
            };

            if (!matches) continue;

            if (isDown)
            {
                if (_activeRadios.Add(binding.RadioId))
                    PTTStateChanged?.Invoke(binding.RadioId, true);
            }
            else
            {
                if (_activeRadios.Remove(binding.RadioId))
                    PTTStateChanged?.Invoke(binding.RadioId, false);
            }
        }
    }

    // Checks if a trigger is currently "held" (for modifier support)
    private bool IsHeld(InputTrigger t)
    {
        return t.DeviceType switch
        {
            InputDeviceType.Keyboard => (Win32.GetAsyncKeyState(
                System.Windows.Input.KeyInterop.VirtualKeyFromKey(t.KeyboardKey ?? WpfKey.None)) & 0x8000) != 0,
            InputDeviceType.Mouse    => (Win32.GetAsyncKeyState(t.MouseVk) & 0x8000) != 0,
            InputDeviceType.Joystick =>
                _joyBtnState.GetValueOrDefault((t.JoystickGuid, t.JoystickButton), false),
            _ => false
        };
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _pollRunning = false;
        if (_kbHook    != nint.Zero) Win32.UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != nint.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        foreach (var (dev, _, _) in _joysticks)
            try { dev.Unacquire(); dev.Dispose(); } catch { }
        _di?.Dispose();
        _kbHook = _mouseHook = nint.Zero;
    }

    // ── Win32 ─────────────────────────────────────────────────────────────────

    private static class Win32
    {
        public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);
        public delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nint dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public int ptX, ptY;
            public uint mouseData, flags, time;
            public nint dwExtraInfo;
        }

        [DllImport("user32.dll")] public static extern nint SetWindowsHookEx(int id, LowLevelKeyboardProc fn, nint hMod, uint tid);
        [DllImport("user32.dll")] public static extern nint SetWindowsHookEx(int id, LowLevelMouseProc fn, nint hMod, uint tid);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint h);
        [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint h, int n, nint w, nint l);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("kernel32.dll")] public static extern nint GetModuleHandle(string? n);

        public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        public const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
        public const int WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
    }
}
