using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WolfNETRadio.ViewModels;

namespace WolfNETRadio.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();
    }
}
