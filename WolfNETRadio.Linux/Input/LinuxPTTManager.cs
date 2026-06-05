using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace WolfNETRadio.Input;

/// <summary>
/// Linux PTT manager using libevdev for global keyboard/mouse/joystick input.
///
/// Works on both X11 and Wayland — reads directly from /dev/input/eventX.
/// Requires the user to be in the `input` group:
///   sudo usermod -aG input $USER
///
/// Falls back to Avalonia window-focused events for users without input access.
/// </summary>
public class LinuxPTTManager : IDisposable
{
    private List<PttBinding> _bindings = [];
    private List<ChannelSwitchBinding> _switchBindings = [];
    private readonly HashSet<int> _activeRadios = [];
    private volatile bool _running;
    private Thread? _pollThread;

    public event Action<int, bool>? PTTStateChanged;
    public event Action<int>?       ChannelSwitchRequested;

    // Track previous key states per device: fd -> set of pressed keys
    private readonly Dictionary<int, HashSet<int>> _deviceKeyState = [];

    public void SetBindings(IReadOnlyList<PttBinding> bindings)       => _bindings = bindings.ToList();
    public void SetSwitchBindings(IReadOnlyList<ChannelSwitchBinding> b) => _switchBindings = b.ToList();

    /// <summary>Returns (JoyId=fd, Name) for detected joystick devices.</summary>
    public List<(int JoyId, string Name)> GetJoystickDevices()
    {
        var result = new List<(int, string)>();
        try
        {
            foreach (var path in Directory.GetFiles("/dev/input", "js*").OrderBy(x => x))
            {
                result.Add((result.Count, Path.GetFileName(path)));
            }
        }
        catch { }
        return result;
    }

    public void InstallHook()
    {
        if (_running) return;
        _running = true;
        _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "WolfNET-EvdevPoll" };
        _pollThread.Start();
    }

    private void PollLoop()
    {
        // Open all accessible /dev/input/event* devices
        var devices = new List<(int fd, string path)>();
        try
        {
            foreach (var path in Directory.GetFiles("/dev/input", "event*").OrderBy(x => x))
            {
                int fd = Libc.open(path, Libc.O_RDONLY | Libc.O_NONBLOCK);
                if (fd >= 0) devices.Add((fd, path));
            }
        }
        catch { }

        if (devices.Count == 0)
        {
            // No access to /dev/input — instruct caller to fall back to Avalonia events
            return;
        }

        var inputEvent = new EvdevInputEvent();
        int structSize = Marshal.SizeOf<EvdevInputEvent>();
        var buf = new byte[structSize];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);

        try
        {
            while (_running)
            {
                bool anyActivity = false;
                foreach (var (fd, _) in devices)
                {
                    int rd = Libc.read(fd, buf, structSize);
                    if (rd != structSize) continue;
                    anyActivity = true;

                    inputEvent = Marshal.PtrToStructure<EvdevInputEvent>(handle.AddrOfPinnedObject());

                    // EV_KEY = 1
                    if (inputEvent.type != 1) continue;

                    int  keyCode = inputEvent.code;
                    bool pressed = inputEvent.value != 0;

                    if (!_deviceKeyState.TryGetValue(fd, out var pressed_set))
                    {
                        pressed_set = [];
                        _deviceKeyState[fd] = pressed_set;
                    }

                    if (pressed) pressed_set.Add(keyCode);
                    else         pressed_set.Remove(keyCode);

                    HandleKey(keyCode, pressed);
                }
                if (!anyActivity) Thread.Sleep(4); // ~250Hz max poll
            }
        }
        finally
        {
            handle.Free();
            foreach (var (fd, _) in devices)
                Libc.close(fd);
        }
    }

    private void HandleKey(int keyCode, bool pressed)
    {
        foreach (var binding in _bindings)
        {
            // Check modifier (if set, must be held)
            if (binding.PrimaryModifier?.DeviceType == InputDeviceType.Keyboard)
            {
                var modKey = LinuxKeyMap.ToEvdev(binding.PrimaryModifier.KeyboardKey);
                if (modKey > 0 && !IsKeyHeld(modKey)) continue;
            }

            if (binding.Primary?.DeviceType == InputDeviceType.Keyboard)
            {
                var pKey = LinuxKeyMap.ToEvdev(binding.Primary.KeyboardKey);
                if (pKey == keyCode)
                {
                    if (pressed) { if (_activeRadios.Add(binding.RadioId)) PTTStateChanged?.Invoke(binding.RadioId, true); }
                    else         { if (_activeRadios.Remove(binding.RadioId)) PTTStateChanged?.Invoke(binding.RadioId, false); }
                }
            }
            if (binding.Secondary?.DeviceType == InputDeviceType.Keyboard)
            {
                if (binding.SecondaryModifier?.DeviceType == InputDeviceType.Keyboard)
                {
                    var modKey = LinuxKeyMap.ToEvdev(binding.SecondaryModifier.KeyboardKey);
                    if (modKey > 0 && !IsKeyHeld(modKey)) goto checkSwitch;
                }
                var sKey = LinuxKeyMap.ToEvdev(binding.Secondary.KeyboardKey);
                if (sKey == keyCode)
                {
                    if (pressed) { if (_activeRadios.Add(binding.RadioId)) PTTStateChanged?.Invoke(binding.RadioId, true); }
                    else         { if (_activeRadios.Remove(binding.RadioId)) PTTStateChanged?.Invoke(binding.RadioId, false); }
                }
            }
            checkSwitch:;
        }

        if (pressed)
        {
            foreach (var sw in _switchBindings)
            {
                if (sw.SwitchTrigger?.DeviceType == InputDeviceType.Keyboard)
                {
                    var swKey = LinuxKeyMap.ToEvdev(sw.SwitchTrigger.KeyboardKey);
                    if (swKey == keyCode) ChannelSwitchRequested?.Invoke(sw.RadioId);
                }
            }
        }
    }

    private bool IsKeyHeld(int evdevCode)
    {
        foreach (var set in _deviceKeyState.Values)
            if (set.Contains(evdevCode)) return true;
        return false;
    }

    public void Dispose() { _running = false; }

    // ── Evdev struct ─────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct EvdevInputEvent
    {
        public long  sec;
        public long  usec;
        public ushort type;
        public ushort code;
        public int   value;
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    private static class Libc
    {
        public const int O_RDONLY   = 0;
        public const int O_NONBLOCK = 0x800;

        [DllImport("libc", EntryPoint = "open",  SetLastError = true)]
        public static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

        [DllImport("libc", EntryPoint = "read",  SetLastError = true)]
        public static extern int read(int fd, [Out] byte[] buf, int count);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int close(int fd);
    }
}
