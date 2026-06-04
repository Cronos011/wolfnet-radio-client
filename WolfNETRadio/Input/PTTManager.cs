using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace WolfNETRadio.Input;

public class PTTManager : IDisposable
{
    // PTT bindings: radioId -> Key
    private readonly Dictionary<int, Key> _bindings = [];
    private readonly HashSet<int> _activeRadios = [];

    public event Action<int, bool>? PTTStateChanged; // (radioId, isPressed)

    private nint _hookId = nint.Zero;
    private Win32.LowLevelKeyboardProc? _proc;

    public void SetBinding(int radioId, Key key)
    {
        _bindings[radioId] = key;
    }

    public void InstallHook()
    {
        if (_hookId != nint.Zero)
            return;

        _proc = HookCallback;
        using var process = Process.GetCurrentProcess();
        var module = process.MainModule;
        var moduleHandle = Win32.GetModuleHandle(module?.ModuleName);
        _hookId = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, moduleHandle, 0);
        if (_hookId == nint.Zero)
            throw new InvalidOperationException("Failed to install keyboard hook.");
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            if (msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN ||
                msg == Win32.WM_KEYUP || msg == Win32.WM_SYSKEYUP)
            {
                var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                var key = KeyInterop.KeyFromVirtualKey((int)info.vkCode);
                var isDown = msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN;
                foreach (var binding in _bindings)
                {
                    if (binding.Value != key)
                        continue;

                    if (isDown)
                    {
                        if (_activeRadios.Add(binding.Key))
                            PTTStateChanged?.Invoke(binding.Key, true);
                    }
                    else
                    {
                        if (_activeRadios.Remove(binding.Key))
                            PTTStateChanged?.Invoke(binding.Key, false);
                    }
                }
            }
        }

        return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != nint.Zero)
            Win32.UnhookWindowsHookEx(_hookId);
    }

    private static class Win32
    {
        public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public nint dwExtraInfo;
        }

        [DllImport("user32.dll")] public static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hhk);
        [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);
        [DllImport("kernel32.dll")] public static extern nint GetModuleHandle(string? lpModuleName);

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
    }
}
