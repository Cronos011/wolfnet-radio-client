using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using WpfKey = System.Windows.Input.Key;

namespace WolfNETRadio.Input;

/// <summary>
/// Manages PTT bindings across keyboard, mouse, and joystick/HOTAS devices.
/// 
/// - Keyboard + mouse: Win32 low-level hooks (WH_KEYBOARD_LL + WH_MOUSE_LL)
///   works even when the app window is not focused.
/// - Joystick/HOTAS: WinMM joyGetPosEx polling on a background thread.
///   Covers all HID joystick-class devices (HOTAS, flight sticks, throttles,
///   gamepads, etc.) up to 16 devices with 32 buttons each. No external package.
/// </summary>
public class PTTManager : IDisposable
{
    private List<PttBinding> _bindings = [];
    private readonly HashSet<int> _activeRadios = [];
    public event Action<int, bool>? PTTStateChanged;

    // ── Keyboard hook ────────────────────────────────────────────────────────
    private nint _kbHook = nint.Zero;
    private Win32.LowLevelKeyboardProc? _kbProc;

    // ── Mouse hook ───────────────────────────────────────────────────────────
    private nint _mouseHook = nint.Zero;
    private Win32.LowLevelMouseProc? _mouseProc;

    // ── Joystick polling (WinMM) ─────────────────────────────────────────────
    private Thread? _pollThread;
    private volatile bool _pollRunning;
    // Track previous button states: [joyId][buttonBit]
    private readonly Dictionary<int, uint> _joyPrevButtons = [];

    // ── Public API ───────────────────────────────────────────────────────────

    public void SetBindings(IReadOnlyList<PttBinding> bindings)
        => _bindings = bindings.ToList();

    /// <summary>
    /// Returns a list of connected joystick devices found via WinMM.
    /// Each entry is (joyId 0-15, name).
    /// </summary>
    public List<(int JoyId, string Name)> GetJoystickDevices()
    {
        var result = new List<(int, string)>();
        for (int id = 0; id < WinMM.MAXJOYSTICKS; id++)
        {
            var caps = new WinMM.JOYCAPS();
            if (WinMM.joyGetDevCaps(id, ref caps, Marshal.SizeOf<WinMM.JOYCAPS>()) == 0)
                result.Add((id, caps.szPname));
        }
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
        _kbProc = KbCallback;
        using var proc = Process.GetCurrentProcess();
        _kbHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _kbProc,
            Win32.GetModuleHandle(proc.MainModule?.ModuleName), 0);
    }

    private nint KbCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var msg    = wParam.ToInt32();
            var isDown = msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN;
            var isUp   = msg == Win32.WM_KEYUP   || msg == Win32.WM_SYSKEYUP;
            if (isDown || isUp)
            {
                var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                var key  = System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)info.vkCode);
                HandleTrigger(InputDeviceType.Keyboard, keyboardKey: key, isDown: isDown);
            }
        }
        return Win32.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    // ── Mouse ────────────────────────────────────────────────────────────────

    private void InstallMouseHook()
    {
        if (_mouseHook != nint.Zero) return;
        _mouseProc = MouseCallback;
        using var proc = Process.GetCurrentProcess();
        _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc,
            Win32.GetModuleHandle(proc.MainModule?.ModuleName), 0);
    }

    private nint MouseCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            int? vk = null; bool? dn = null;
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
                    var s = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                    var xb = (s.mouseData >> 16) & 0xFFFF;
                    vk = xb == 1 ? 5 : 6;
                    dn = msg == Win32.WM_XBUTTONDOWN;
                    break;
            }
            if (vk.HasValue && dn.HasValue)
                HandleTrigger(InputDeviceType.Mouse, mouseVk: vk.Value, isDown: dn.Value);
        }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    // ── Joystick polling (WinMM joyGetPosEx) ─────────────────────────────────

    private void StartJoystickPoll()
    {
        if (_pollRunning) return;
        _pollRunning = true;
        _pollThread = new Thread(JoyPollLoop) { IsBackground = true, Name = "WolfNET-JoyPoll" };
        _pollThread.Start();
    }

    private void JoyPollLoop()
    {
        while (_pollRunning)
        {
            for (int id = 0; id < WinMM.MAXJOYSTICKS; id++)
            {
                var info = new WinMM.JOYINFOEX { dwSize = Marshal.SizeOf<WinMM.JOYINFOEX>(), dwFlags = (int)WinMM.JOY_RETURNALL };
                if (WinMM.joyGetPosEx(id, ref info) != 0) continue;

                var prev = _joyPrevButtons.GetValueOrDefault(id, 0u);
                var cur  = info.dwButtons;
                var changed = prev ^ cur;

                for (int btn = 0; btn < 32; btn++)
                {
                    uint mask = 1u << btn;
                    if ((changed & mask) == 0) continue;
                    var pressed = (cur & mask) != 0;
                    HandleTrigger(InputDeviceType.Joystick, joyId: id, joyBtn: btn, isDown: pressed);
                }

                _joyPrevButtons[id] = cur;
            }
            Thread.Sleep(8); // ~120 Hz
        }
    }

    // ── Unified trigger matcher ───────────────────────────────────────────────

    private void HandleTrigger(
        InputDeviceType deviceType,
        WpfKey keyboardKey = WpfKey.None,
        int mouseVk = 0,
        int joyId = 0,
        int joyBtn = 0,
        bool isDown = false)
    {
        foreach (var binding in _bindings)
        {
            if (binding.Modifier != null && !IsHeld(binding.Modifier)) continue;

            var t = binding.Primary;
            if (t == null) continue;

            bool hit = t.DeviceType == deviceType && deviceType switch
            {
                InputDeviceType.Keyboard => (t.KeyboardKey ?? WpfKey.None) == keyboardKey,
                InputDeviceType.Mouse    => t.MouseVk == mouseVk,
                InputDeviceType.Joystick => t.JoystickId == joyId && t.JoystickButton == joyBtn,
                _ => false
            };
            if (!hit) continue;

            if (isDown) { if (_activeRadios.Add(binding.RadioId))    PTTStateChanged?.Invoke(binding.RadioId, true); }
            else        { if (_activeRadios.Remove(binding.RadioId))  PTTStateChanged?.Invoke(binding.RadioId, false); }
        }
    }

    private bool IsHeld(InputTrigger t) => t.DeviceType switch
    {
        InputDeviceType.Keyboard => (Win32.GetAsyncKeyState(
            System.Windows.Input.KeyInterop.VirtualKeyFromKey(t.KeyboardKey ?? WpfKey.None)) & 0x8000) != 0,
        InputDeviceType.Mouse    => (Win32.GetAsyncKeyState(t.MouseVk) & 0x8000) != 0,
        InputDeviceType.Joystick =>
            _joyPrevButtons.TryGetValue(t.JoystickId, out var b) && (b & (1u << t.JoystickButton)) != 0,
        _ => false
    };

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _pollRunning = false;
        if (_kbHook    != nint.Zero) Win32.UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != nint.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        _kbHook = _mouseHook = nint.Zero;
    }

    // ── Win32 + WinMM ─────────────────────────────────────────────────────────

    private static class Win32
    {
        public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);
        public delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nint dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT { public int ptX, ptY; public uint mouseData, flags, time; public nint dwExtraInfo; }

        [DllImport("user32.dll")] public static extern nint SetWindowsHookEx(int id, LowLevelKeyboardProc fn, nint h, uint t);
        [DllImport("user32.dll")] public static extern nint SetWindowsHookEx(int id, LowLevelMouseProc fn, nint h, uint t);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint h);
        [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint h, int n, nint w, nint l);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("kernel32.dll")] public static extern nint GetModuleHandle(string? n);

        public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        public const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        public const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
        public const int WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
        public const int WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208;
        public const int WM_XBUTTONDOWN = 0x20B, WM_XBUTTONUP = 0x20C;
    }

    private static class WinMM
    {
        public const int MAXJOYSTICKS = 16;
        public const uint JOY_RETURNALL = 0xFF;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct JOYCAPS
        {
            public ushort wMid, wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons;
            public uint wPeriodMin, wPeriodMax;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOYINFOEX
        {
            public int  dwSize, dwFlags;
            public uint dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
            public uint dwButtons, dwButtonNumber, dwPOV;
            public uint dwReserved1, dwReserved2;
        }

        [DllImport("winmm.dll")] public static extern int joyGetDevCaps(int id, ref JOYCAPS caps, int size);
        [DllImport("winmm.dll")] public static extern int joyGetPosEx(int id, ref JOYINFOEX info);
    }
}
