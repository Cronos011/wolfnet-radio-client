using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using WolfNETRadio.Audio;
using WolfNETRadio.Input;
using WolfNETRadio.Models;
using WolfNETRadio.Network;
using WolfNETRadio.ViewModels;

namespace WolfNETRadio.Views;

public partial class MainWindow : Window
{
    private MainViewModel _vm = null!;
    private LinuxAudioInputManager  _audioIn  = null!;
    private LinuxAudioOutputManager _audioOut = null!;
    private LinuxPTTManager         _ptt      = null!;
    private GwReconAuthClient       _auth     = null!;
    private KeyBindingStore         _keyStore = null!;

    private List<MissionPreset> _presets = [];
    private readonly System.Timers.Timer _vuTimer = new(50) { AutoReset = true };
    private readonly AppSettings _settings = AppSettings.Load();

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _vm       = App.Services.GetRequiredService<MainViewModel>();
        _audioIn  = App.Services.GetRequiredService<LinuxAudioInputManager>();
        _audioOut = App.Services.GetRequiredService<LinuxAudioOutputManager>();
        _ptt      = App.Services.GetRequiredService<LinuxPTTManager>();
        _auth     = App.Services.GetRequiredService<GwReconAuthClient>();
        _keyStore = App.Services.GetRequiredService<KeyBindingStore>();

        DataContext = _vm;

        // Restore settings
        _settings.ApplyTo(ClientState.Instance);
        ServerAddressBox.Text = _settings.ServerAddress;
        RadioKeyBox.Text      = _settings.RadioAccessKey;

        // Populate audio devices
        foreach (var d in _audioIn.GetInputDevices())  InputDeviceCombo.Items.Add(d);
        foreach (var d in _audioOut.GetOutputDevices()) OutputDeviceCombo.Items.Add(d);
        SelectDevice(InputDeviceCombo,  _settings.InputDeviceName);
        SelectDevice(OutputDeviceCombo, _settings.OutputDeviceName);

        InputDeviceCombo.SelectionChanged  += (_, _) => _vm.SelectedInputDevice  = InputDeviceCombo.SelectedItem?.ToString();
        OutputDeviceCombo.SelectionChanged += (_, _) => _vm.SelectedOutputDevice = OutputDeviceCombo.SelectedItem?.ToString();

        // Wire PTT
        _ptt.SetBindings(_keyStore.Bindings);
        _ptt.SetSwitchBindings(_keyStore.SwitchBindings);
        _ptt.PTTStateChanged        += (id, pressed) => _vm.OnPTTState(id, pressed);
        _ptt.ChannelSwitchRequested += id => Dispatcher.UIThread.Post(() => ClientState.Instance.Radios[id].SwitchChannel());
        _ptt.InstallHook();

        // VU meter
        _vm.PropertyChanged += (_, e2) =>
        {
            if (e2.PropertyName == nameof(MainViewModel.VuLevel))
                Dispatcher.UIThread.Post(() => VuBar.Value = (double)_vm.VuLevel * 100.0);
        };

        // Client count
        _vm.PropertyChanged += (_, e2) =>
        {
            if (e2.PropertyName == nameof(MainViewModel.ConnectedClientCount))
                Dispatcher.UIThread.Post(() => ClientCountLabel.Text = _vm.ConnectedClientCount.ToString());
        };

        // Build radio slot list
        BuildRadioSlots();

        // Intercom binding
        var intercom = ClientState.Instance.Radios[0];
        intercom.PropertyChanged += (_, e2) =>
        {
            if (e2.PropertyName is nameof(RadioSlot.ChannelCode) or nameof(RadioSlot.DisplayCode))
                Dispatcher.UIThread.Post(() => IntercomCode.Text = intercom.DisplayCode);
            if (e2.PropertyName == nameof(RadioSlot.IsTransmitting))
                Dispatcher.UIThread.Post(() => IntercomTxLabel.IsVisible = intercom.IsTransmitting);
            if (e2.PropertyName is nameof(RadioSlot.IsReceiving) or nameof(RadioSlot.ReceivingCallsign))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IntercomRxLabel.IsVisible = intercom.IsReceiving;
                    IntercomRxLabel.Text      = $"▼ {intercom.ReceivingCallsign}";
                });
            }
        };
        IntercomCode.Text = intercom.DisplayCode;
    }

    private void SelectDevice(ComboBox cb, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) && cb.Items.Count > 0) { cb.SelectedIndex = 0; return; }
        int idx = cb.Items.OfType<string>().ToList().IndexOf(name ?? "");
        cb.SelectedIndex = idx >= 0 ? idx : (cb.Items.Count > 0 ? 0 : -1);
    }

    private void BuildRadioSlots()
    {
        RadioSlotList.Items.Clear();
        for (int i = 1; i <= 10; i++)
        {
            var slot = ClientState.Instance.Radios[i];
            var view = new Controls.RadioSlotControl { DataContext = slot };
            RadioSlotList.Items.Add(view);
        }
    }

    // ── Connect ──────────────────────────────────────────────────────────

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.IsConnected)
        {
            _vm.Disconnect();
            ConnectButton.Content = "CONNECT";
            ConnectButton.Foreground = Avalonia.Media.Brushes.Green;
            StatusLabel.Text = "Disconnected";
            return;
        }
        ClientState.Instance.ServerAddress   = ServerAddressBox.Text ?? "gwrecon.com:5002";
        ClientState.Instance.RadioAccessKey  = RadioKeyBox.Text ?? "";
        _vm.SelectedInputDevice  = InputDeviceCombo.SelectedItem?.ToString();
        _vm.SelectedOutputDevice = OutputDeviceCombo.SelectedItem?.ToString();

        ConnectButton.IsEnabled = false;
        StatusLabel.Text = "Connecting…";
        try
        {
            await _vm.ConnectAsync();
            ConnectButton.Content   = "DISCONNECT";
            ConnectButton.Foreground = Avalonia.Media.Brushes.Red;
            StatusLabel.Text = $"Connected as {ClientState.Instance.Callsign}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
        ConnectButton.IsEnabled = true;
    }

    // ── Audio test ───────────────────────────────────────────────────────

    private void AudioTest_Down(object? s, PointerPressedEventArgs e)
    {
        _audioOut.StartPassthrough();
        AudioTestButton.Content   = "🎙 TESTING — RELEASE TO STOP";
        AudioTestButton.Foreground = Avalonia.Media.Brushes.Green;
    }
    private void AudioTest_Up(object? s, PointerReleasedEventArgs e)
    {
        _audioOut.StopPassthrough();
        AudioTestButton.Content   = "🎙 HOLD TO TEST MIC";
        AudioTestButton.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8A96A8"));
    }

    // ── Presets ──────────────────────────────────────────────────────────

    private async void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        var key = RadioKeyBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(key)) return;
        RefreshButton.IsEnabled = false;
        RefreshButton.Content   = "…";
        _presets = await _auth.GetMissionsAsync(key);
        MissionCombo.Items.Clear();
        foreach (var p in _presets) MissionCombo.Items.Add(p.MissionName);
        if (MissionCombo.Items.Count > 0) MissionCombo.SelectedIndex = 0;
        LoadAllButton.IsEnabled = _presets.Count > 0;
        RefreshButton.Content   = "REFRESH";
        RefreshButton.IsEnabled = true;
    }

    private void MissionCombo_Changed(object? sender, SelectionChangedEventArgs e)
    {
        int idx = MissionCombo.SelectedIndex;
        if (idx < 0 || idx >= _presets.Count) return;
        var mission = _presets[idx];
        PresetList.Items.Clear();

        if (mission.Intercom is { } ic)
            PresetList.Items.Add(MakePresetRow($"[{ic.Code:D4}] {ic.Label}", "→ INTERCOM", "#43E59A",
                () => ClientState.Instance.Radios[0].ChannelCode = ic.Code));

        foreach (var ch in mission.Channels.OrderBy(c => c.Slot))
        {
            var capturedCh = ch;
            PresetList.Items.Add(MakePresetRow($"[{ch.Code:D4}] {ch.Label}", $"→ RADIO {ch.Slot}", "#4FC3F7",
                () =>
                {
                    if (capturedCh.Slot >= 1 && capturedCh.Slot <= 10)
                        ClientState.Instance.Radios[capturedCh.Slot].ChannelCode = capturedCh.Code;
                }));
        }
        LoadAllButton.IsEnabled = true;
    }

    private void LoadAll_Click(object? sender, RoutedEventArgs e)
    {
        int idx = MissionCombo.SelectedIndex;
        if (idx < 0 || idx >= _presets.Count) return;
        var m = _presets[idx];
        if (m.Intercom is { } ic) ClientState.Instance.Radios[0].ChannelCode = ic.Code;
        foreach (var ch in m.Channels)
            if (ch.Slot >= 1 && ch.Slot <= 10)
                ClientState.Instance.Radios[ch.Slot].ChannelCode = ch.Code;
    }

    private static Border MakePresetRow(string label, string slot, string color, Action onClick)
    {
        var border = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0C1119")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E2A3A")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(8, 5),
            Margin = new Avalonia.Thickness(0, 2),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = label, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color)), FontFamily = new Avalonia.Media.FontFamily("Consolas,monospace"), FontSize = 11 });
        var slotLabel = new TextBlock { Text = slot, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8A96A8")), FontFamily = new Avalonia.Media.FontFamily("Consolas,monospace"), FontSize = 10, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Grid.SetColumn(slotLabel, 1);
        grid.Children.Add(slotLabel);
        border.Child = grid;
        border.PointerPressed += (_, _) => onClick();
        return border;
    }

    // ── Close ────────────────────────────────────────────────────────────

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var s = new AppSettings();
        s.SnapshotFrom(ClientState.Instance);
        s.Save();
        _ptt.Dispose();
    }
}
