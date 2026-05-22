using ExplainMyPC.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace ExplainMyPC;

/// <summary>
/// The application shell window.
/// Handles sidebar navigation by switching pages in a Frame.
/// Also manages the lifecycle of the floating CompanionWindow.
/// </summary>
public sealed partial class MainWindow : Window
{
    // ── Companion overlay ──────────────────────────────────────────────────────

    private CompanionWindow? _companionWindow;
    private bool             _companionVisible;

    // ── Constructor ────────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.Resize(new SizeInt32(1280, 820));
        Title = "ExplainMyPC";

        ContentFrame.Navigate(typeof(DashboardPage));
    }

    // ── Sidebar navigation ─────────────────────────────────────────────────────

    /// <summary>
    /// When a sidebar item is clicked, navigate to the matching page.
    /// The Tag on each NavigationViewItem tells us which page to load.
    /// </summary>
    private void NavView_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: string tag })
        {
            NavigateToPage(tag);
        }
    }

    /// <summary>
    /// Simple switch that maps sidebar tag strings to page types.
    /// </summary>
    private void NavigateToPage(string tag)
    {
        Type? pageType = tag switch
        {
            "dashboard"     => typeof(DashboardPage),
            "explain"       => typeof(ExplainPage),
            "insights"      => typeof(InsightsPage),
            "security"      => typeof(SecurityPage),
            "processes"     => typeof(ProcessesPage),
            "storage"       => typeof(StoragePage),
            "apps"          => typeof(AppsPage),
            "privacy"       => typeof(PrivacyPage),
            "repairs"       => typeof(RepairsPage),
            "timeline"      => typeof(TimelinePage),
            "replay"        => typeof(ReplayPage),
            "historical"    => typeof(HistoricalPage),
            "behavior"      => typeof(MachineBehaviorPage),
            "devices"       => typeof(DeviceIntelligencePage),
            "governance"    => typeof(RuntimeGovernancePage),
            "watchtower"    => typeof(WatchtowerPage),
            "remediation"   => typeof(AutonomousRemediationPage),
            "simulation"    => typeof(SimulationPage),
            "diagnostics"   => typeof(DiagnosticsPage),
            "investigation" => typeof(InvestigationPage),
            "settings"      => typeof(SettingsPage),
            _ => null
        };

        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    // ── Companion window toggle ────────────────────────────────────────────────

    /// <summary>
    /// Toggles the Companion overlay window.
    /// Creates it on first use; subsequently shows/hides it.
    /// The companion is a separate window — it persists across page navigations.
    /// </summary>
    private void NavCompanion_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ToggleCompanion();
    }

    private void ToggleCompanion()
    {
        if (_companionWindow is null)
        {
            // Create on first use
            _companionWindow = new CompanionWindow();
            _companionWindow.Activate();
            _companionVisible = true;
            return;
        }

        // Toggle visibility via the AppWindow presenter
        // (WinUI 3 has no direct Window.Visibility — use Minimize/Restore pattern)
        if (_companionVisible)
        {
            _companionWindow.AppWindow.Hide();
            _companionVisible = false;
        }
        else
        {
            _companionWindow.AppWindow.Show();
            _companionVisible = true;
        }
    }
}
