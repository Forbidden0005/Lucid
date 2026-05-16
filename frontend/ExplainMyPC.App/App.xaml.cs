using Microsoft.UI.Xaml;

namespace ExplainMyPC;

/// <summary>
/// Application entry point.
/// Simplified — no dependency injection, no services.
/// Just launches the main window.
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
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }
}
