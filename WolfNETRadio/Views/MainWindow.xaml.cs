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
        foreach (var binding in _keyStore.Bindings)
        {
            var row = BuildBindingRow(binding);
            KeyBindingRows.Children.Add(row);
        }
    }

    private UIElement BuildBindingRow(PttBinding binding)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var dark = new SolidColorBrush(Color.FromRgb(0x0C, 0x11, 0x19));
        var border = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x3A));
        var gold = new SolidColorBrush(Color.FromRgb(0xD4, 0xA0, 0x17));
        var muted = new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xA8));
        var cyan = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
        var font = new FontFamily("Consolas");

        var label = new TextBlock
        {
            Text = binding.RadioLabel,
            FontSize = 11,
            Foreground = muted,
            FontFamily = font,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var primaryBox = new TextBox
        {
            Text = binding.PrimaryKeyDisplay,
            IsReadOnly = true,
            Background = dark,
            Foreground = binding.PrimaryKey.HasValue ? cyan : muted,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            FontFamily = font,
            FontSize = 11,
            Height = 24,
            TextAlignment = TextAlignment.Center
        };
        Grid.SetColumn(primaryBox, 1);
        grid.Children.Add(primaryBox);

        var setBtn = new Button
        {
            Content = "SET",
            Height = 24,
            Margin = new Thickness(4, 0, 0, 0),
            Background = dark,
            Foreground = gold,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            FontFamily = font,
            FontSize = 9
        };
        Grid.SetColumn(setBtn, 2);
        var capturedRadioId = binding.RadioId;
        setBtn.Click += (_, _) => StartCapture(capturedRadioId, false, setBtn, primaryBox);
        grid.Children.Add(setBtn);

        var clearBtn = new Button
        {
            Content = "CLR",
            Height = 24,
            Margin = new Thickness(2, 0, 0, 0),
            Background = dark,
            Foreground = muted,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            FontFamily = font,
            FontSize = 9
        };
        Grid.SetColumn(clearBtn, 3);
        clearBtn.Click += (_, _) =>
        {
            _keyStore.ClearPrimary(capturedRadioId);
            primaryBox.Text = "[UNBOUND]";
            primaryBox.Foreground = muted;
        };
        grid.Children.Add(clearBtn);

        var modBox = new TextBox
        {
            Text = binding.ModifierKeyDisplay,
            IsReadOnly = true,
            Background = dark,
            Foreground = binding.ModifierKey.HasValue ? cyan : muted,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            FontFamily = font,
            FontSize = 11,
            Height = 24,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(modBox, 5);
        grid.Children.Add(modBox);

        var setModBtn = new Button
        {
            Content = "SET",
            Height = 24,
            Margin = new Thickness(4, 0, 0, 0),
            Background = dark,
            Foreground = gold,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            FontFamily = font,
            FontSize = 9
        };
        Grid.SetColumn(setModBtn, 6);
        setModBtn.Click += (_, _) => StartCapture(capturedRadioId, true, setModBtn, modBox);
        grid.Children.Add(setModBtn);

        var clearModBtn = new Button
        {
            Content = "CLR",
            Height = 24,
            Margin = new Thickness(2, 0, 0, 0),
            Background = dark,
            Foreground = muted,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            FontFamily = font,
            FontSize = 9
        };
        Grid.SetColumn(clearModBtn, 7);
        clearModBtn.Click += (_, _) =>
        {
            _keyStore.ClearModifier(capturedRadioId);
            modBox.Text = "None";
            modBox.Foreground = muted;
        };
        grid.Children.Add(clearModBtn);

        return grid;
    }

    private void StartCapture(int radioId, bool modifier, Button btn, TextBox display)
    {
        _captureRadioId = radioId;
        _captureModifier = modifier;
        btn.Content = "...";
        display.Text = "Press a key...";
        display.Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xA0, 0x17));
        PreviewKeyDown += CaptureKeyHandler;
        Focus();
    }

    private void CaptureKeyHandler(object sender, System.Windows.Input.KeyEventArgs e)
    {
        PreviewKeyDown -= CaptureKeyHandler;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;

        if (_captureModifier)
            _keyStore.SetModifier(_captureRadioId, key);
        else
            _keyStore.SetPrimary(_captureRadioId, key);

        if (!_captureModifier)
        {
            var vm = (MainViewModel)DataContext;
        }

        BuildKeyBindingRows();
        e.Handled = true;
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
        RefreshPresetsButton.Content = "REFRESH";
        RefreshPresetsButton.IsEnabled = true;
    }

    private void MissionPresetsCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var idx = MissionPresetsCombo.SelectedIndex;
        if (idx < 0 || idx >= _loadedPresets.Count) return;
        var mission = _loadedPresets[idx];

        PresetChannelsList.Items.Clear();
        foreach (var ch in mission.Channels)
        {
            var border = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x0C, 0x11, 0x19)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x1E, 0x2A, 0x3A)),
                BorderThickness = new System.Windows.Thickness(1),
                Padding = new System.Windows.Thickness(8, 4, 8, 4),
                Margin = new System.Windows.Thickness(0, 2, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var label = new System.Windows.Controls.TextBlock
            {
                Text = $"[{ch.Code:D4}] {ch.Label}  →  Slot {ch.Slot}",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4F, 0xC3, 0xF7)),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11
            };
            border.Child = label;
            // Click to apply preset to that slot
            var capturedCh = ch;
            border.MouseDown += (_, _) =>
            {
                var vm = (MainViewModel)DataContext;
                if (capturedCh.Slot >= 0 && capturedCh.Slot < vm.RadioSlots.Length)
                    vm.RadioSlots[capturedCh.Slot - 1].CommitChannelCode(capturedCh.Code.ToString());
            };
            PresetChannelsList.Items.Add(border);
        }
    }

}
