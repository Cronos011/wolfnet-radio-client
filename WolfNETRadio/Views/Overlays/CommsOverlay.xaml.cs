using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WolfNETRadio.ViewModels;

namespace WolfNETRadio.Views.Overlays;

public partial class CommsOverlay : Window
{
    public CommsOverlay()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<CommsOverlayViewModel>();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
