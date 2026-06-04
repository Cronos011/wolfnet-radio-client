using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.Extensions.DependencyInjection;
using WolfNETRadio.ViewModels;
using WolfNETRadio.Views.Overlays;

namespace WolfNETRadio.Views;

public partial class MainWindow : Window
{
    private CommsOverlay? _commsOverlay;
    private CommandCenter? _commandCenter;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();
        var vm = (MainViewModel)DataContext;
        RadioAccessKeyBox.Text = vm.RadioAccessKey;
        ServerAddressBox.Text = vm.ServerAddress;
        RadioAccessKeyBox.TextChanged += (_, _) => vm.RadioAccessKey = RadioAccessKeyBox.Text;
        ServerAddressBox.TextChanged += (_, _) => vm.ServerAddress = ServerAddressBox.Text;
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
        _ = vm.IsConnected ? Task.Run(vm.DisconnectCommand.ExecuteAsync) : vm.ConnectCommand.ExecuteAsync(null);
    }
}
