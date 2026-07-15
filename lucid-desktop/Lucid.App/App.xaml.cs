using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lucid;

/// <summary>
/// Application entry point.
///
/// Responsibilities:
///   • Initialize app-level services (AppServices.Initialize) before the window opens.
///   • Shut down services cleanly when the main window closes.
///   • Last-resort crash logging: without these hooks a fatal XAML/WinRT
///     exception kills the process with 0xC000027B (stowed exception) and
///     leaves NOTHING in Lucid's own logs — startup crashes become
///     undiagnosable. Confirmed in the field on 2026-07-02.
/// </summary>
public partial class App : Application
{
    private MainWindow? _mainWindow;

    /// <summary>
    /// Resolves an application singleton from the service registry.
    ///
    /// This is the composition entry point for pages and ViewModels
    /// (ROADMAP C4): WinUI's Frame.Navigate constructs pages through their
    /// parameterless constructor, so pages resolve their ViewModel
    /// dependencies here instead of reading AppServices.* statics. App and
    /// AppServices are the only types that may touch the static locator —
    /// a source-audit test freezes everything else.
    /// </summary>
    public static T GetService<T>() where T : class =>
        AppServices.Registry.Get<T>();

    public App()
    {
        // Crash visibility hooks — registered BEFORE InitializeComponent so a
        // failure loading App.xaml resources (malformed dictionary, stale XAML
        // intermediates) is also captured. These log and let the process die
        // honestly — no swallowing, no fake recovery. Note: exceptions raised
        // natively inside XAML can bypass all managed hooks; for those,
        // Windows Error Reporting remains the only trace.
        UnhandledException += (_, e) =>
            TryLogFatal("XAML UnhandledException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            TryLogFatal("AppDomain UnhandledException",
                e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown non-Exception object"));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            TryLogFatal("UnobservedTaskException", e.Exception);

        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            TryLogFatal("App.InitializeComponent failed (App.xaml resource load)", ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Initialize services on the UI thread so they can capture the
            // DispatcherQueue used to marshal telemetry back to the UI.
            AppServices.Initialize(DispatcherQueue.GetForCurrentThread());

            _mainWindow = new MainWindow();

            // Release PerformanceCounter handles and background tasks when the
            // window closes. Shutdown must never crash the exit: log any
            // teardown failure and let the process end cleanly.
            _mainWindow.Closed += (_, _) =>
            {
                try { AppServices.Shutdown(); }
                catch (Exception ex) { TryLogFatal("Shutdown failed on window close", ex); }
            };

            _mainWindow.Activate();
        }
        catch (Exception ex)
        {
            // Startup is the highest-value place to capture a fatal error:
            // log it, then rethrow — the app must not limp on half-launched.
            TryLogFatal("Startup (OnLaunched) failed", ex);
            throw;
        }
    }

    /// <summary>Serializes crash-log writes — cascading failures can fire the
    /// hooks concurrently on different threads, and an unsynchronized append
    /// would drop one of the records.</summary>
    private static readonly object _crashLogLock = new();

    /// <summary>
    /// Appends a fatal-error record to %LOCALAPPDATA%\Lucid\logs\crash.log using
    /// raw file I/O only — the structured logger may be the thing that failed,
    /// and last-resort logging must never itself throw.
    /// </summary>
    private static void TryLogFatal(string source, Exception? ex)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Lucid", "logs", "crash.log");

            lock (_crashLogLock)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.AppendAllText(path,
                    $"[{DateTimeOffset.Now:O}] {source}: {ex}\n\n");
            }
        }
        catch
        {
            // Nothing left to do — never throw from the crash logger.
        }
    }
}
