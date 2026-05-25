using Lucid.Services.Companion;
using Lucid.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Lucid;

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
        Title = "Lucid";

        ContentFrame.Navigate(typeof(DashboardPage));

        // Ensure the overlay window is created whenever ANY code makes the
        // companion visible — Dashboard button, page deep-links, automation, etc.
        // StateChanged fires synchronously on the UI thread so this is safe.
        AppServices.CompanionSession.StateChanged += (_, e) =>
        {
            if (e.NewState != CompanionOverlayState.Hidden)
                EnsureCompanionWindow();
        };
    }

    // ── Companion window lifecycle ─────────────────────────────────────────────

    /// <summary>
    /// Creates the CompanionOverlayWindow on first use and wires it up.
    /// Safe to call multiple times — no-ops if the window already exists.
    /// </summary>
    private void EnsureCompanionWindow()
    {
        if (_companionWindow is not null) return;

        _companionWindow = new CompanionOverlayWindow();

        // Clear the cached reference when the OS destroys the window (Alt+F4, etc.)
        // so the next Show()/Expand() re-creates it cleanly.
        _companionWindow.Closed += (_, _) => _companionWindow = null;

        // Forward suggested-action navigation from the companion to the main frame.
        _companionWindow.NavigationRequested += (_, tag) => NavigateToPage(tag);

        _companionWindow.Activate();
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
    /// Sidebar "Companion" tap — toggles the overlay between Hidden/Bubble/Expanded.
    /// Window creation is handled by the StateChanged subscription in the constructor.
    /// </summary>
    private void NavCompanion_Tapped(object sender, TappedRoutedEventArgs e)
    {
        AppServices.CompanionSession.ToggleExpanded();
        // EnsureCompanionWindow() already ran inside the StateChanged handler above
        // (synchronously, before ToggleExpanded returns) if the new state is visible.
        _companionWindow?.Activate();
    }
}
