using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lucid;

/// <summary>
/// Application entry point.
///
/// Responsibilities:
///   • Initialize app-level services (AppServices.Initialize) before the window opens.
///   • Shut down services cleanly when the main window closes.
/// </summary>
public partial class App : Application
{
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Initialize services on the UI thread so they can capture the
        // DispatcherQueue used to marshal telemetry back to the UI.
        AppServices.Initialize(DispatcherQueue.GetForCurrentThread());

        _mainWindow = new MainWindow();

        // Release PerformanceCounter handles and background tasks when the window closes.
        _mainWindow.Closed += (_, _) => AppServices.Shutdown();

        _mainWindow.Activate();
    }
}
