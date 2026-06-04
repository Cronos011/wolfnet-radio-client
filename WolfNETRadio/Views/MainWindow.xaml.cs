using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using WolfNETRadio.Audio;
using WolfNETRadio.ViewModels;
using WolfNETRadio.Views.Overlays;

namespace WolfNETRadio.Views;

public partial class MainWindow : Window
{
    private CommsOverlay? _commsOverlay;
    private CommandCenter? _commandCenter;
    private readonly AudioInputManager _audioIn;
    private readonly AudioOutputManager _audioOut;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();
        _audioIn = App.Services.GetRequiredService<AudioInputManager>();
        _audioOut = App.Services.GetRequiredService<AudioOutputManager>();

        var vm = (MainViewModel)DataContext;

        // Sync text fields
        RadioAccessKeyBox.Text = vm.RadioAccessKey;
        ServerAddressBox.Text = vm.ServerAddress;
        RadioAccessKeyBox.TextChanged += (_, _) => vm.RadioAccessKey = RadioAccessKeyBox.Text;
        ServerAddressBox.TextChanged += (_, _) => vm.ServerAddress = ServerAddressBox.Text;

        // Populate audio device dropdowns
        foreach (var d in _audioIn.GetInputDevices())
            InputDeviceCombo.Items.Add(d);
        if (InputDeviceCombo.Items.Count > 0) InputDeviceCombo.SelectedIndex = 0;

        foreach (var d in _audioOut.GetOutputDevices())
            OutputDeviceCombo.Items.Add(d);
        if (OutputDeviceCombo.Items.Count > 0) OutputDeviceCombo.SelectedIndex = 0;

        InputDeviceCombo.SelectionChanged += (_, _) =>
            vm.SelectedInputDevice = InputDeviceCombo.SelectedItem?.ToString();
        OutputDeviceCombo.SelectionChanged += (_, _) =>
            vm.SelectedOutputDevice = OutputDeviceCombo.SelectedItem?.ToString();

        // Wire settings toggles to ViewModel
        RadioEffectsCheck.Checked += (_, _) => vm.RadioEffectsEnabled = true;
        RadioEffectsCheck.Unchecked += (_, _) => vm.RadioEffectsEnabled = false;

        SpeakerVolumeSlider.ValueChanged += (_, e) => vm.SpeakerVolume = (float)(e.NewValue / 100.0);
        MicBoostSlider.ValueChanged += (_, e) => vm.MicVolume = (float)(e.NewValue / 100.0);

        AlwaysOnTopCheck.Checked += (_, _) => { if (_commsOverlay != null) _commsOverlay.Topmost = true; if (_commandCenter != null) _commandCenter.Topmost = true; };
        AlwaysOnTopCheck.Unchecked += (_, _) => { if (_commsOverlay != null) _commsOverlay.Topmost = false; if (_commandCenter != null) _commandCenter.Topmost = false; };

        // Wire VU meter from ViewModel
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.VuLevel))
                Dispatcher.Invoke(() => VuMeter.Level = vm.VuLevel);
        };

        // Wire connection state to UI
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ServerStatus) or nameof(MainViewModel.IsConnected) or nameof(MainViewModel.StatusMessage))
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

        // Connect button
        ConnectButton.Content = connected ? "DISCONNECT" : "CONNECT";
        ConnectButton.Background = connected
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44))
            : new SolidColorBrush(Color.FromRgb(0xD4, 0xA0, 0x17));
        ConnectButton.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x0C));
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

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (vm.IsConnected)
            vm.DisconnectCommand.Execute(null);
        else
            _ = vm.ConnectCommand.ExecuteAsync(null);
    }
}
