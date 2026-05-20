using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ExplainMyPC.Views;

/// <summary>
/// Intelligence Workspace page — full-page view of active findings, trend
/// analysis, cross-insight correlations, and operation history.
///
/// Code-behind responsibilities (intentionally thin):
///   ViewModel — instantiated here; exposed as a public property so
///               the XAML can reach it via x:Bind.
///
///   Cleanup   — Page.Unloaded unsubscribes from the app-level intelligence
///               service to prevent event-handler leaks on navigation.
///
/// No animation helpers on this page — insight cards are scrollable and
/// numerous; per-card hover animations would be expensive at the scale
/// this page handles (up to 12+ cards with full action panels).
/// </summary>
public sealed partial class InsightsPage : Page
{
    public InsightsPageViewModel ViewModel { get; } = new InsightsPageViewModel();

    public InsightsPage()
    {
        InitializeComponent();

        // Wire the navigation callback so insight cards can drill into the
        // InsightDetailPage without a hard Frame reference in the ViewModel.
        ViewModel.SetNavigationCallback(id =>
            Frame.Navigate(typeof(InsightDetailPage), id,
                           new DrillInNavigationTransitionInfo()));

        Unloaded += (_, _) => ViewModel.Cleanup();
    }
}
