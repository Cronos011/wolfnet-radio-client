using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using WolfNETRadio.Audio;
using WolfNETRadio.Input;
using WolfNETRadio.Network;
using WolfNETRadio.ViewModels;

namespace WolfNETRadio;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core singletons
        services.AddSingleton<Models.ClientState>(_ => Models.ClientState.Instance);
        services.AddSingleton<SRSControlClient>();
        services.AddSingleton<SRSVoiceClient>();
        services.AddSingleton<GwReconAuthClient>();
        services.AddSingleton<AudioInputManager>();
        services.AddSingleton<AudioOutputManager>();
        services.AddSingleton<OpusProcessor>();
        services.AddSingleton<PTTManager>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<CommsOverlayViewModel>();
        services.AddSingleton<CommandCenterViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
