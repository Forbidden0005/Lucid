using ExplainMyPC.Core.Navigation;
using ExplainMyPC.Intelligence;
using ExplainMyPC.Services;
using ExplainMyPC.ViewModels;
using ExplainMyPC.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace ExplainMyPC;

public partial class App : Application
{
    /// <summary>
    /// Global DI container. Resolved once at startup; never mutated after that.
    /// Pages call App.GetService&lt;T&gt;() in their constructors because WinUI's
    /// Frame.Navigate() instantiates pages by reflection, bypassing DI constructor
    /// injection. Keeping this seam explicit is intentional — it is easy to audit
    /// and easy to replace with a proper container-aware frame in the future.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
        Services = BuildServiceProvider();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Logging — debug output in development, no-op in release.
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));

        // ── Navigation ────────────────────────────────────────────────────────
        // Singleton: one NavigationService holds the Frame reference for the
        // lifetime of the shell window.
        services.AddSingleton<INavigationService, NavigationService>();

        // ── Services ──────────────────────────────────────────────────────────
        // ITelemetryService is singleton: one shared stream for all consumers.
        // WindowsTelemetryService reads real hardware data via Windows Performance
        // Counters and the kernel32 GlobalMemoryStatusEx API.
        // To use simulated data during UI development, swap in MockTelemetryService.
        services.AddSingleton<ITelemetryService, WindowsTelemetryService>();
        // services.AddSingleton<ITelemetryService, MockTelemetryService>();

        // Intelligence Engine — singleton because it is stateless (all state lives
        // in the rules' input context, not the engine itself).
        services.AddSingleton<IIntelligenceEngine, IntelligenceEngine>();

        // ── ViewModels ────────────────────────────────────────────────────────
        // ShellViewModel is singleton (same lifetime as MainWindow).
        // Page ViewModels are Transient: each navigation produces a fresh VM.
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ExplainViewModel>();
        services.AddTransient<SecurityViewModel>();
        services.AddTransient<ProcessesViewModel>();
        services.AddTransient<StorageViewModel>();
        services.AddTransient<AppsViewModel>();
        services.AddTransient<PrivacyViewModel>();
        services.AddTransient<RepairsViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }

    /// <summary>
    /// Convenience resolver used by pages that cannot receive injected
    /// dependencies because WinUI creates them through Frame.Navigate().
    /// </summary>
    public static T GetService<T>() where T : class
    {
        if (Services.GetService(typeof(T)) is not T service)
            throw new InvalidOperationException(
                $"{typeof(T).FullName} is not registered in the DI container.");
        return service;
    }
}
