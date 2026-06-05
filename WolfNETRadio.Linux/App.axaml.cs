using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using WolfNETRadio.Audio;
using WolfNETRadio.Input;
using WolfNETRadio.Models;
using WolfNETRadio.Network;
using WolfNETRadio.ViewModels;
using WolfNETRadio.Views;

namespace WolfNETRadio;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ClientState>(_ => ClientState.Instance);
        services.AddSingleton<SRSControlClient>();
        services.AddSingleton<SRSVoiceClient>();
        services.AddSingleton<GwReconAuthClient>();
        services.AddSingleton<LinuxAudioInputManager>();
        services.AddSingleton<LinuxAudioOutputManager>();
        services.AddSingleton<OpusProcessor>();
        services.AddSingleton<LinuxPTTManager>();
        services.AddSingleton<KeyBindingStore>();
        services.AddSingleton<MainViewModel>();
    }
}
