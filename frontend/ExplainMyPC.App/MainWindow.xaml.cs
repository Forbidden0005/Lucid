using ExplainMyPC.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace ExplainMyPC;

/// <summary>
/// The application shell window.
/// Handles sidebar navigation by switching pages in a Frame.
/// Also manages the lifecycle of the floating CompanionOverlayWindow.
/// </summary>
public sealed partial class MainWindow : Window
{
    // ── Companion overlay ──────────────────────────────────────────────────────

    private CompanionOverlayWindow? _companionWindow;

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
    /// Creates it on first use; subsequently the window manages its own
    /// visibility in response to ICompanionSessionManager.StateChanged.
    /// </summary>
    private void NavCompanion_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_companionWindow is null)
        {
            // Create on first use — window subscribes to StateChanged in its constructor.
            _companionWindow = new CompanionOverlayWindow();

            // Drive initial state transition (Hidden → Expanded).
            // CompanionOverlayWindow.OnSessionStateChanged handles the resize + show.
            AppServices.CompanionSession.ToggleExpanded();

            // Activate so the window receives focus and appears on screen.
            _companionWindow.Activate();
        }
        else
        {
            // Window exists — delegate toggle to the session manager.
            // The window's StateChanged handler resizes and shows/hides itself.
            AppServices.CompanionSession.ToggleExpanded();
        }
    }
}
