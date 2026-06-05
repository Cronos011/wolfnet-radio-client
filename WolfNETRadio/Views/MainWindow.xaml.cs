using WolfNETRadio.Network;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using WolfNETRadio.Audio;
using WolfNETRadio.ViewModels;
using WolfNETRadio.Views.Overlays;
using WolfNETRadio.Input;
using WolfNETRadio.Models;
using System.Linq;

namespace WolfNETRadio.Views;

public partial class MainWindow : Window
{
    private CommsOverlay? _commsOverlay;
    private CommandCenter? _commandCenter;
    private readonly AudioInputManager _audioIn;
    private readonly AudioOutputManager _audioOut;
    private readonly GwReconAuthClient _authClient;
    private readonly PTTManager _pttManager;
    private List<MissionPreset> _loadedPresets = [];
    private readonly KeyBindingStore _keyStore = new();
    private int _captureRadioId = -1;
    private bool _captureModifier = false;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();
        _audioIn = App.Services.GetRequiredService<AudioInputManager>();
        _audioOut = App.Services.GetRequiredService<AudioOutputManager>();
        _authClient = App.Services.GetRequiredService<GwReconAuthClient>();
        _pttManager = App.Services.GetRequiredService<PTTManager>();
        _pttManager.SetBindings(_keyStore.Bindings);
        _pttManager.SetSwitchBindings(_keyStore.SwitchBindings);

        var vm = (MainViewModel)DataContext;

        // ── Restore persisted settings ───────────────────────────────────────
        var settings = AppSettings.Load();
        settings.ApplyTo(ClientState.Instance);

        // Sync text fields from (potentially restored) state
        RadioAccessKeyBox.Text = vm.RadioAccessKey;
        ServerAddressBox.Text = vm.ServerAddress;
        RadioAccessKeyBox.TextChanged += (_, _) => vm.RadioAccessKey = RadioAccessKeyBox.Text;
        ServerAddressBox.TextChanged += (_, _) => vm.ServerAddress = ServerAddressBox.Text;

        // ── Audio device dropdowns ───────────────────────────────────────────
        // IMPORTANT: wire SelectionChanged BEFORE setting SelectedIndex so the
        // ViewModel is updated when the initial selection fires.
        InputDeviceCombo.SelectionChanged += (_, _) =>
            vm.SelectedInputDevice = InputDeviceCombo.SelectedItem?.ToString();
        OutputDeviceCombo.SelectionChanged += (_, _) =>
            vm.SelectedOutputDevice = OutputDeviceCombo.SelectedItem?.ToString();

        // Populate input devices; restore saved selection or default to first
        var inputDevices  = _audioIn.GetInputDevices().ToList();
        foreach (var d in inputDevices) InputDeviceCombo.Items.Add(d);
        if (inputDevices.Count > 0)
        {
            var savedInput = settings.InputDeviceName;
            var savedIdx   = inputDevices.IndexOf(savedInput);
            InputDeviceCombo.SelectedIndex = savedIdx >= 0 ? savedIdx : 0;
        }

        // Populate output devices; restore saved selection or default to first
        var outputDevices = _audioOut.GetOutputDevices().ToList();
        foreach (var d in outputDevices) OutputDeviceCombo.Items.Add(d);
        if (outputDevices.Count > 0)
        {
            var savedOutput = settings.OutputDeviceName;
            var savedIdx    = outputDevices.IndexOf(savedOutput);
            OutputDeviceCombo.SelectedIndex = savedIdx >= 0 ? savedIdx : 0;
        }

        BuildKeyBindingRows();

        // Wire settings toggles to ViewModel + restore persisted values
        RadioEffectsCheck.IsChecked = settings.RadioEffects;
        RadioEffectsCheck.Checked   += (_, _) => vm.RadioEffectsEnabled = true;
        RadioEffectsCheck.Unchecked += (_, _) => vm.RadioEffectsEnabled = false;

        SpeakerVolumeSlider.Value = settings.SpeakerVolume * 100.0;
        MicBoostSlider.Value      = settings.MicVolume * 100.0;
        SpeakerVolumeSlider.ValueChanged += (_, e) => vm.SpeakerVolume = (float)(e.NewValue / 100.0);
        MicBoostSlider.ValueChanged      += (_, e) => vm.MicVolume      = (float)(e.NewValue / 100.0);

        // VOX sliders
        VoxThresholdSlider.Value = settings.VoxThreshold;
        VoxHangSlider.Value      = settings.VoxHangtimeMs;
        UpdateVoxLabels();
        VoxThresholdSlider.ValueChanged += (_, _) => { vm.VoxThreshold = (float)VoxThresholdSlider.Value; UpdateVoxLabels(); };
        VoxHangSlider.ValueChanged      += (_, _) => { vm.VoxHangtimeMs = (int)VoxHangSlider.Value;      UpdateVoxLabels(); };

        AlwaysOnTopCheck.Checked += (_, _) => { if (_commsOverlay != null) _commsOverlay.Topmost = true; if (_commandCenter != null) _commandCenter.Topmost = true; };
        AlwaysOnTopCheck.Unchecked += (_, _) => { if (_commsOverlay != null) _commsOverlay.Topmost = false; if (_commandCenter != null) _commandCenter.Topmost = false; };

        // Wire VU meter from ViewModel
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.VuLevel))
                Dispatcher.Invoke(() => VuMeter.Level = (double)vm.VuLevel * 100.0);
        };

        // Wire connection state to UI
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ServerStatus)
                or nameof(MainViewModel.IsConnected)
                or nameof(MainViewModel.StatusMessage)
                or nameof(MainViewModel.VoipStatus)
                or nameof(MainViewModel.IsVoipConnected))
                Dispatcher.Invoke(UpdateConnectionUI);
        };
    }

    private void UpdateConnectionUI()
    {
        var vm = (MainViewModel)DataContext;
        var connected = vm.IsConnected;

        // Status dot + text
        ServerStatusDot.Fill = connected
            ? new SolidColorBrush(Color.FromRgb(0x43, 0xE5, 0x9A))   // green
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));  // red
        ServerStatusText.Text = connected ? "CONNECTED" : "DISCONNECTED";
        if (StatusLabel != null) StatusLabel.Text = vm.StatusMessage == "Ready" ? "" : vm.StatusMessage;

        // VOIP status
        VoipStatusDot.Fill = vm.IsVoipConnected
            ? new SolidColorBrush(Color.FromRgb(0x43, 0xE5, 0x9A))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
        VoipStatusText.Text = vm.IsVoipConnected ? "VOIP" : "VOIP";
        VoipStatusText.Foreground = vm.IsVoipConnected
            ? new SolidColorBrush(Color.FromRgb(0x43, 0xE5, 0x9A))
            : new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xA8));

        // Connect button
        ConnectButton.Content = connected ? "DISCONNECT" : "CONNECT";
        ConnectButton.Background = connected
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44))
            : new SolidColorBrush(Color.FromRgb(0xD4, 0xA0, 0x17));
        ConnectButton.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x0C));
    }

    private void BuildKeyBindingRows()
    {
        KeyBindingRows.Children.Clear();
        _joystickDevices = _pttManager.GetJoystickDevices();
        foreach (var binding in _keyStore.Bindings)
        {
            var row = BuildBindingRow(binding);
            KeyBindingRows.Children.Add(row);
        }
        // Rebuild channel switch rows too
        BuildSwitchBindingRows();
    }

    private void BuildSwitchBindingRows()
    {
        if (SwitchBindingRows == null) return;
        SwitchBindingRows.Children.Clear();
        foreach (var sb in _keyStore.SwitchBindings)
        {
            var row = BuildSwitchRow(sb);
            SwitchBindingRows.Children.Add(row);
        }
    }

    private UIElement BuildSwitchRow(WolfNETRadio.Input.ChannelSwitchBinding sb)
    {
        var dark   = new SolidColorBrush(Color.FromRgb(0x0C, 0x11, 0x19));
        var border = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x3A));
        var gold   = new SolidColorBrush(Color.FromRgb(0xD4, 0xA0, 0x17));
        var muted  = new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xA8));
        var cyan   = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
        var font   = new FontFamily("Consolas");

        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var label = new TextBlock { Text = sb.RadioLabel, FontSize = 11, Foreground = muted, FontFamily = font, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var trigBox = new TextBox
        {
            Text = sb.SwitchDisplay,
            IsReadOnly = true,
            Background = dark, Foreground = sb.SwitchTrigger != null ? cyan : muted,
            BorderBrush = border, BorderThickness = new Thickness(1),
            FontFamily = font, FontSize = 11, Height = 24, TextAlignment = TextAlignment.Center
        };
        Grid.SetColumn(trigBox, 1);
        grid.Children.Add(trigBox);

        var setBtn = new Button
        {
            Content = "SET", Height = 24, Margin = new Thickness(4, 0, 0, 0),
            Background = dark, Foreground = gold, BorderBrush = border, BorderThickness = new Thickness(1),
            FontFamily = font, FontSize = 9
        };
        Grid.SetColumn(setBtn, 2);
        var capturedId = sb.RadioId;
        setBtn.Click += (_, _) => StartSwitchCapture(capturedId, setBtn, trigBox);
        grid.Children.Add(setBtn);

        var clrBtn = new Button
        {
            Content = "CLR", Height = 24, Margin = new Thickness(2, 0, 0, 0),
            Background = dark, Foreground = muted, BorderBrush = border, BorderThickness = new Thickness(1),
            FontFamily = font, FontSize = 9
        };
        Grid.SetColumn(clrBtn, 3);
        clrBtn.Click += (_, _) =>
        {
            _keyStore.ClearSwitchTrigger(capturedId);
            _pttManager.SetSwitchBindings(_keyStore.SwitchBindings);
            trigBox.Text = "[UNBOUND]";
            trigBox.Foreground = muted;
        };
        grid.Children.Add(clrBtn);
        return grid;
    }

    private void StartSwitchCapture(int radioId, Button btn, TextBox display)
    {
        _captureCts?.Cancel();
        _captureCts = new System.Threading.CancellationTokenSource();
        var cts = _captureCts;
        btn.Content = "...";
        display.Text = "Press key/btn...";
        display.Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xA0, 0x17));
        PreviewKeyDown += (s, e) =>
        {
            cts.Cancel();
            PreviewKeyDown -= null;
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            var t = new WolfNETRadio.Input.InputTrigger { DeviceType = WolfNETRadio.Input.InputDeviceType.Keyboard, KeyboardKey = key };
            _keyStore.SetSwitchTrigger(radioId, t);
            _pttManager.SetSwitchBindings(_keyStore.SwitchBindings);
            BuildSwitchBindingRows();
            btn.Content = "SET";
            e.Handled = true;
        };
        // Also allow mouse/joystick capture asynchronously
        System.Threading.Tasks.Task.Run(async () =>
        {
            var t = await WaitForAnyInputAsync(cts.Token);
            if (t == null) return;
            Dispatcher.Invoke(() =>
            {
                _keyStore.SetSwitchTrigger(radioId, t);
                _pttManager.SetSwitchBindings(_keyStore.SwitchBindings);
                BuildSwitchBindingRows();
                btn.Content = "SET";
            });
        });
        Focus();
    }

    // Cached joystick list (WinMM int id)
    private List<(int JoyId, string Name)> _joystickDevices = [];

    // Capture field enum
    private enum CaptureField { Primary, PrimaryMod, Secondary, SecondaryMod }
    private CaptureField _captureField = CaptureField.Primary;

    private UIElement BuildBindingRow(PttBinding binding)
    {
        // Layout: Label | P-trigger SET CLR | P-mod SET CLR | S-trigger SET CLR | S-mod SET CLR
        // Displayed as two stacked sub-rows inside a StackPanel for readability
        var outer = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
        var dark = new SolidColorBrush(Color.FromRgb(0x0C, 0x11, 0x19));
        var border = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x3A));
        var gold = new SolidColorBrush(Color.FromRgb(0xD4, 0xA0, 0x17));
        var muted = new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xA8));
        var cyan = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
        var amber = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
        var font = new FontFamily("Consolas");
        var capturedId = binding.RadioId;

        // Helper: build a trigger box + SET + CLR group
        (Grid row, TextBox box) MakeTriggerGroup(
            string labelTxt, string display, bool bound,
            Action<Button, TextBox> onSet, Action<TextBox> onClear)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var lbl = new TextBlock { Text = labelTxt, FontSize = 9, Foreground = muted, FontFamily = font, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0); g.Children.Add(lbl);

            var tb = new TextBox
            {
                Text = display, IsReadOnly = true,
                Background = dark, Foreground = bound ? cyan : muted,
                BorderBrush = border, BorderThickness = new Thickness(1),
                FontFamily = font, FontSize = 10, Height = 22, TextAlignment = TextAlignment.Center
            };
            Grid.SetColumn(tb, 1); g.Children.Add(tb);

            var sb2 = new Button { Content = "SET", Height = 22, Margin = new Thickness(2, 0, 0, 0), Background = dark, Foreground = gold, BorderBrush = border, BorderThickness = new Thickness(1), FontFamily = font, FontSize = 8 };
            Grid.SetColumn(sb2, 2); sb2.Click += (_, _) => onSet(sb2, tb); g.Children.Add(sb2);

            var cb2 = new Button { Content = "CLR", Height = 22, Margin = new Thickness(2, 0, 0, 0), Background = dark, Foreground = muted, BorderBrush = border, BorderThickness = new Thickness(1), FontFamily = font, FontSize = 8 };
            Grid.SetColumn(cb2, 3); cb2.Click += (_, _) => onClear(tb); g.Children.Add(cb2);
            return (g, tb);
        }

        // Radio label header
        outer.Children.Add(new TextBlock { Text = binding.RadioLabel, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = gold, FontFamily = font, Margin = new Thickness(0, 2, 0, 2) });

        // Primary trigger
        var (pg, _) = MakeTriggerGroup("PRIMARY:", binding.PrimaryDisplay, binding.Primary != null,
            (btn, box) => StartCapture2(capturedId, CaptureField.Primary, btn, box),
            box => { _keyStore.ClearPrimary(capturedId); box.Text = "[UNBOUND]"; box.Foreground = muted; _pttManager.SetBindings(_keyStore.Bindings); });
        outer.Children.Add(pg);

        // Primary modifier
        var (pm, _) = MakeTriggerGroup("P-MOD:", binding.PrimaryModDisplay, binding.PrimaryModifier != null,
            (btn, box) => StartCapture2(capturedId, CaptureField.PrimaryMod, btn, box),
            box => { _keyStore.ClearPrimaryModifier(capturedId); box.Text = "None"; box.Foreground = muted; _pttManager.SetBindings(_keyStore.Bindings); });
        outer.Children.Add(pm);

        // Secondary trigger
        var (sg, _) = MakeTriggerGroup("SECONDARY:", binding.SecondaryDisplay, binding.Secondary != null,
            (btn, box) => StartCapture2(capturedId, CaptureField.Secondary, btn, box),
            box => { _keyStore.ClearSecondary(capturedId); box.Text = "[UNBOUND]"; box.Foreground = muted; _pttManager.SetBindings(_keyStore.Bindings); });
        outer.Children.Add(sg);

        // Secondary modifier
        var (smg, _) = MakeTriggerGroup("S-MOD:", binding.SecondaryModDisplay, binding.SecondaryModifier != null,
            (btn, box) => StartCapture2(capturedId, CaptureField.SecondaryMod, btn, box),
            box => { _keyStore.ClearSecondaryModifier(capturedId); box.Text = "None"; box.Foreground = muted; _pttManager.SetBindings(_keyStore.Bindings); });
        outer.Children.Add(smg);

        return outer;

    }

    // Capture state
    private System.Threading.CancellationTokenSource? _captureCts;

    // Legacy StartCapture (still used by old keyboard capture handler) — delegates to StartCapture2
    private void StartCapture(int radioId, bool modifier, Button btn, TextBox display)
        => StartCapture2(radioId, modifier ? CaptureField.PrimaryMod : CaptureField.Primary, btn, display);

    private void StartCapture2(int radioId, CaptureField field, Button btn, TextBox display)
    {
        _captureCts?.Cancel();
        _captureCts = new System.Threading.CancellationTokenSource();
        var cts = _captureCts;

        _captureRadioId  = radioId;
        _captureField    = field;
        _captureModifier = field == CaptureField.PrimaryMod || field == CaptureField.SecondaryMod;
        btn.Content      = "...";
        display.Text     = "Press key/btn/joy...";
        display.Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xA0, 0x17));

        PreviewKeyDown += CaptureKeyHandler;

        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var trigger = await WaitForAnyInputAsync(cts.Token);
                if (trigger == null) return;
                Dispatcher.Invoke(() =>
                {
                    PreviewKeyDown -= CaptureKeyHandler;
                    ApplyCapture(trigger);
                    BuildKeyBindingRows();
                    _pttManager.SetBindings(_keyStore.Bindings);
                });
            }
            catch (OperationCanceledException) { }
        });
        Focus();
    }

    private void ApplyCapture(WolfNETRadio.Input.InputTrigger trigger)
    {
        switch (_captureField)
        {
            case CaptureField.Primary:      _keyStore.SetPrimary(_captureRadioId,          trigger); break;
            case CaptureField.PrimaryMod:   _keyStore.SetPrimaryModifier(_captureRadioId,  trigger); break;
            case CaptureField.Secondary:    _keyStore.SetSecondary(_captureRadioId,         trigger); break;
            case CaptureField.SecondaryMod: _keyStore.SetSecondaryModifier(_captureRadioId, trigger); break;
        }
    }

    /// <summary>
    /// Polls for mouse button or joystick button press in the background.
    /// Returns null if cancelled. Used alongside PreviewKeyDown for keyboard.
    /// </summary>
    private async System.Threading.Tasks.Task<WolfNETRadio.Input.InputTrigger?> WaitForAnyInputAsync(
        System.Threading.CancellationToken ct)
    {
        // Snapshot button states so we only fire on NEW presses
        var startMouseState = GetCurrentMouseButtons();   // Dictionary<int,bool>
        var startJoyState   = SnapshotJoyState();          // Dictionary<(int,int),bool>

        while (!ct.IsCancellationRequested)
        {
            // Check mouse
            foreach (var vk in new[] { 2, 4, 5, 6 }) // RBtn, MBtn, X1, X2 (skip LBtn for usability)
            {
                var now = (Win32Native.GetAsyncKeyState(vk) & 0x8000) != 0;
                if (now && !startMouseState.GetValueOrDefault(vk))
                    return new WolfNETRadio.Input.InputTrigger
                        { DeviceType = WolfNETRadio.Input.InputDeviceType.Mouse, MouseVk = vk };
            }

            // Check joystick
            foreach (var entry in _joystickDevices)
            {
                if (!TryPollJoystick(entry.JoyId, out uint btns)) continue;
                for (int b = 0; b < 32; b++)
                {
                    var key = (entry.JoyId, b);
                    var pressed = (btns & (1u << b)) != 0;
                    if (pressed && !startJoyState.GetValueOrDefault(key))
                        return new WolfNETRadio.Input.InputTrigger
                        {
                            DeviceType    = WolfNETRadio.Input.InputDeviceType.Joystick,
                            JoystickId    = entry.JoyId,
                            JoystickName  = entry.Name,
                            JoystickButton = b
                        };
                }
            }

            await System.Threading.Tasks.Task.Delay(16, ct);
        }
        return null;
    }

    private Dictionary<int, bool> GetCurrentMouseButtons()
    {
        var d = new Dictionary<int, bool>();
        foreach (var vk in new[] { 1, 2, 4, 5, 6 })
            d[vk] = (Win32Native.GetAsyncKeyState(vk) & 0x8000) != 0;
        return d;
    }

    private Dictionary<(int, int), bool> SnapshotJoyState()
    {
        var snap = new Dictionary<(int, int), bool>();
        foreach (var entry in _joystickDevices)
        {
            if (!TryPollJoystick(entry.JoyId, out uint btns)) continue;
            for (int b = 0; b < 32; b++)
                snap[(entry.JoyId, b)] = (btns & (1u << b)) != 0;
        }
        return snap;
    }

    private bool TryPollJoystick(int joyId, out uint buttons)
    {
        buttons = 0;
        try
        {
            var info = new WinMMCapture.JOYINFOEX
                { dwSize = System.Runtime.InteropServices.Marshal.SizeOf<WinMMCapture.JOYINFOEX>(), dwFlags = 0xFF };
            if (WinMMCapture.joyGetPosEx(joyId, ref info) != 0) return false;
            buttons = info.dwButtons;
            return true;
        }
        catch { return false; }
    }

    private static class WinMMCapture
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct JOYINFOEX
        {
            public int dwSize, dwFlags;
            public uint dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
            public uint dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }
        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        public static extern int joyGetPosEx(int id, ref JOYINFOEX info);
    }

    private void CaptureKeyHandler(object sender, System.Windows.Input.KeyEventArgs e)
    {
        _captureCts?.Cancel(); // stop background mouse/joy capture
        PreviewKeyDown -= CaptureKeyHandler;
        var key     = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var trigger = new WolfNETRadio.Input.InputTrigger
            { DeviceType = WolfNETRadio.Input.InputDeviceType.Keyboard, KeyboardKey = key };

        ApplyCapture(trigger);
        _pttManager.SetBindings(_keyStore.Bindings);
        BuildKeyBindingRows();
        e.Handled = true;
    }

    // ── Audio Test ────────────────────────────────────────────────

    private void AudioTest_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var audioOut = App.Services.GetRequiredService<WolfNETRadio.Audio.AudioOutputManager>();
        audioOut.StartPassthrough();
        AudioTestButton.Content = "🎙 TESTING — RELEASE TO STOP";
        AudioTestButton.Foreground = new SolidColorBrush(Color.FromRgb(0x43, 0xE5, 0x9A));
        AudioTestButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x43, 0xE5, 0x9A));
    }

    private void AudioTest_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var audioOut = App.Services.GetRequiredService<WolfNETRadio.Audio.AudioOutputManager>();
        audioOut.StopPassthrough();
        AudioTestButton.Content = "🎙 HOLD TO TEST MIC";
        AudioTestButton.Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xA8));
        AudioTestButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x3A));
    }

    // ── VOX Sliders ────────────────────────────────────────────────

    private void VoxThreshold_Changed(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e) { }
    private void VoxHang_Changed(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e) { }

    private void UpdateVoxLabels()
    {
        if (VoxThresholdLabel != null)
            VoxThresholdLabel.Text = VoxThresholdSlider.Value.ToString("F2");
        if (VoxHangLabel != null)
            VoxHangLabel.Text = ((int)VoxHangSlider.Value).ToString();
    }

    private static class Win32Native
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vk);
    }

    private void CommsArrayButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_commsOverlay == null || !_commsOverlay.IsLoaded)
        {
            _commsOverlay = new CommsOverlay();
            _commsOverlay.DataContext = App.Services.GetRequiredService<CommsOverlayViewModel>();
            _commsOverlay.Closed += (_, _) =>
            {
                _commsOverlay = null;
                CommsArrayButton.IsChecked = false;
            };
        }
        _commsOverlay.Show();
        _commsOverlay.Activate();
    }

    private void CommsArrayButton_Unchecked(object sender, RoutedEventArgs e)
        => _commsOverlay?.Hide();

    private void CommandCenterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_commandCenter == null || !_commandCenter.IsLoaded)
        {
            _commandCenter = new CommandCenter();
            _commandCenter.DataContext = App.Services.GetRequiredService<CommandCenterViewModel>();
            _commandCenter.Closed += (_, _) =>
            {
                _commandCenter = null;
                CommandCenterButton.IsChecked = false;
            };
        }
        _commandCenter.Show();
        _commandCenter.Activate();
    }

    private void CommandCenterButton_Unchecked(object sender, RoutedEventArgs e)
        => _commandCenter?.Hide();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Persist settings on every close
        var settings = new AppSettings();
        settings.SnapshotFrom(ClientState.Instance);
        settings.Save();
        base.OnClosing(e);
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (vm.IsConnected)
            vm.DisconnectCommand.Execute(null);
        else
            _ = vm.ConnectCommand.ExecuteAsync(null);
    }
    private async void RefreshPresets_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (string.IsNullOrWhiteSpace(vm.RadioAccessKey)) return;

        RefreshPresetsButton.IsEnabled = false;
        RefreshPresetsButton.Content = "...";
        _loadedPresets = await _authClient.GetMissionsAsync(vm.RadioAccessKey);
        MissionPresetsCombo.Items.Clear();
        foreach (var m in _loadedPresets)
            MissionPresetsCombo.Items.Add(m.MissionName);
        if (MissionPresetsCombo.Items.Count > 0) MissionPresetsCombo.SelectedIndex = 0;
        LoadAllPresetsButton.IsEnabled = _loadedPresets.Count > 0;
        RefreshPresetsButton.Content = "REFRESH";
        RefreshPresetsButton.IsEnabled = true;
    }

    // Assignment map: channel slot index -> (radioId 1-10, isChannelA)
    private readonly Dictionary<int, (int RadioId, bool IsChannelA)> _assignmentMap = [];

    private void ResetAssignmentMap(int channelCount)
    {
        _assignmentMap.Clear();
        // Default: slot 1 -> RADIO 1 CH A, slot 2 -> RADIO 1 CH B,
        //          slot 3 -> RADIO 2 CH A, slot 4 -> RADIO 2 CH B, ...
        for (int s = 1; s <= channelCount; s++)
        {
            int radio = (s + 1) / 2;           // 1,2->1  3,4->2  5,6->3 ...
            bool isA  = (s % 2) != 0;          // odd=A, even=B
            _assignmentMap[s] = (Math.Min(radio, 10), isA);
        }
    }

    private void MissionPresetsCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var idx = MissionPresetsCombo.SelectedIndex;
        if (idx < 0 || idx >= _loadedPresets.Count) return;
        var mission = _loadedPresets[idx];

        ResetAssignmentMap(mission.Channels.Count);
        PresetChannelsList.Items.Clear();

        var darkC  = System.Windows.Media.Color.FromRgb(0x0C, 0x11, 0x19);
        var borC   = System.Windows.Media.Color.FromRgb(0x1E, 0x2A, 0x3A);
        var cyanC  = System.Windows.Media.Color.FromRgb(0x4F, 0xC3, 0xF7);
        var greenC = System.Windows.Media.Color.FromRgb(0x43, 0xE5, 0x9A);
        var mutedC = System.Windows.Media.Color.FromRgb(0x8A, 0x96, 0xA8);
        var darkBr = new System.Windows.Media.SolidColorBrush(darkC);
        var borBr  = new System.Windows.Media.SolidColorBrush(borC);
        var mono   = new System.Windows.Media.FontFamily("Consolas");
        var segoe  = new System.Windows.Media.FontFamily("Segoe UI");

        var vm = (MainViewModel)DataContext;

        // Intercom row
        if (mission.Intercom is { } ic)
        {
            var b = new System.Windows.Controls.Border
            {
                Background = darkBr, BorderBrush = borBr, BorderThickness = new System.Windows.Thickness(1),
                Padding = new System.Windows.Thickness(8, 4, 8, 4), Margin = new System.Windows.Thickness(0, 2, 0, 0)
            };
            var row = new System.Windows.Controls.Grid();
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            row.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"[{ic.Code:D4}] {ic.Label}",
                Foreground = new System.Windows.Media.SolidColorBrush(greenC),
                FontFamily = mono, FontSize = 11
            });
            var ict = new System.Windows.Controls.TextBlock
            {
                Text = "→ INTERCOM",
                Foreground = new System.Windows.Media.SolidColorBrush(mutedC),
                FontFamily = mono, FontSize = 10, VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(ict, 1);
            row.Children.Add(ict);
            b.Child = row;
            b.MouseDown += (_, _) => vm.IntercomSlot.CommitChannelCode(ic.Code.ToString());
            PresetChannelsList.Items.Add(b);
        }

        // Op channel rows with assignment dropdowns
        foreach (var ch in mission.Channels.OrderBy(c => c.Slot))
        {
            var capturedSlot = ch.Slot;
            var b = new System.Windows.Controls.Border
            {
                Background = darkBr, BorderBrush = borBr, BorderThickness = new System.Windows.Thickness(1),
                Padding = new System.Windows.Thickness(6, 3, 6, 3), Margin = new System.Windows.Thickness(0, 2, 0, 0)
            };
            var row = new System.Windows.Controls.Grid();
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(70) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(44) });

            row.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"[{ch.Code:D4}] {ch.Label}",
                Foreground = new System.Windows.Media.SolidColorBrush(cyanC),
                FontFamily = mono, FontSize = 11, VerticalAlignment = System.Windows.VerticalAlignment.Center
            });

            // Radio assignment combo
            var radioCb = new System.Windows.Controls.ComboBox
            {
                Height = 22, FontFamily = segoe, FontSize = 10,
                Background = new System.Windows.Media.SolidColorBrush(darkC),
                Foreground = new System.Windows.Media.SolidColorBrush(cyanC),
                BorderBrush = borBr, Margin = new System.Windows.Thickness(4, 0, 0, 0)
            };
            for (int r = 1; r <= 10; r++) radioCb.Items.Add($"R{r}");
            radioCb.SelectedIndex = (_assignmentMap.TryGetValue(capturedSlot, out var asgn) ? asgn.RadioId : 1) - 1;
            System.Windows.Controls.Grid.SetColumn(radioCb, 1);
            row.Children.Add(radioCb);

            // A/B combo
            var abCb = new System.Windows.Controls.ComboBox
            {
                Height = 22, FontFamily = segoe, FontSize = 10,
                Background = new System.Windows.Media.SolidColorBrush(darkC),
                Foreground = new System.Windows.Media.SolidColorBrush(cyanC),
                BorderBrush = borBr, Margin = new System.Windows.Thickness(2, 0, 0, 0)
            };
            abCb.Items.Add("CH A"); abCb.Items.Add("CH B");
            abCb.SelectedIndex = (asgn.IsChannelA) ? 0 : 1;
            System.Windows.Controls.Grid.SetColumn(abCb, 2);
            row.Children.Add(abCb);

            // Update assignment map when user changes combo
            radioCb.SelectionChanged += (_, _) =>
            {
                var r = radioCb.SelectedIndex + 1;
                var isA = abCb.SelectedIndex == 0;
                _assignmentMap[capturedSlot] = (r, isA);
            };
            abCb.SelectionChanged += (_, _) =>
            {
                var r = radioCb.SelectedIndex + 1;
                var isA = abCb.SelectedIndex == 0;
                _assignmentMap[capturedSlot] = (r, isA);
            };

            b.Child = row;
            // Click row border to apply this single channel
            b.MouseDown += (_, bme) =>
            {
                if (bme.OriginalSource is System.Windows.Controls.ComboBox ||  
                    bme.OriginalSource is System.Windows.Controls.ComboBoxItem) return;
                if (!_assignmentMap.TryGetValue(capturedSlot, out var a)) return;
                var slotVm = vm.RadioSlots[a.RadioId - 1];
                if (a.IsChannelA) slotVm.ChannelACode = ch.Code;
                else slotVm.ChannelBCode = ch.Code;
                if (slotVm.IsChannelA != a.IsChannelA) slotVm.SwitchChannel();
            };
            PresetChannelsList.Items.Add(b);
        }

        LoadAllPresetsButton.IsEnabled = true;
    }

    private void LoadAllPresets_Click(object sender, RoutedEventArgs e)
    {
        var idx = MissionPresetsCombo.SelectedIndex;
        if (idx < 0 || idx >= _loadedPresets.Count) return;
        var mission = _loadedPresets[idx];
        var vm = (MainViewModel)DataContext;

        // Load intercom
        if (mission.Intercom is { } ic)
            vm.IntercomSlot.CommitChannelCode(ic.Code.ToString());

        // Load all op channels using assignment map
        foreach (var ch in mission.Channels)
        {
            if (!_assignmentMap.TryGetValue(ch.Slot, out var asgn)) continue;
            if (asgn.RadioId < 1 || asgn.RadioId > vm.RadioSlots.Length) continue;
            var slotVm = vm.RadioSlots[asgn.RadioId - 1];
            if (asgn.IsChannelA) slotVm.ChannelACode = ch.Code;
            else slotVm.ChannelBCode = ch.Code;
        }
    }

}
