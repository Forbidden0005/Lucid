using Lucid.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

/// <summary>
/// Health-score breakdown workspace. Reached by clicking the System Health
/// card on the Dashboard. Thin code-behind: the ViewModel computes the
/// breakdown; this handles navigation only.
/// </summary>
public sealed partial class HealthBreakdownPage : Page
{
    public HealthBreakdownViewModel ViewModel { get; } =
        new HealthBreakdownViewModel(AppServices.HistoricalAnalytics);

    public HealthBreakdownPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            await ViewModel.LoadAsync();
        }
        catch
        {
            // LoadAsync is already best-effort; guard the async-void boundary
            // so a compute failure can never crash the process.
        }
    }

    private void OnGoBackClick(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    // Each contribution's "Review & fix" deep-links to the insight detail page,
    // where the recommended actions / fix flow already live.
    private void OnReviewClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string insightId } &&
            !string.IsNullOrWhiteSpace(insightId))
        {
            Frame.Navigate(typeof(InsightDetailPage), insightId,
                new Microsoft.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo());
        }
    }
}
