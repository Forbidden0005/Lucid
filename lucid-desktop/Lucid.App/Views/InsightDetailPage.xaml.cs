using CommunityToolkit.Mvvm.DependencyInjection;
using Lucid.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

/// <summary>
/// Code-behind for the InsightDetailPage investigation workspace.
///
/// Responsibilities (intentionally thin — all logic lives in the ViewModel):
///   1. Expose <see cref="ViewModel"/> for x:Bind in XAML.
///   2. Forward the navigated insight ID to <see cref="InsightDetailViewModel.Load"/>
///      on <see cref="OnNavigatedTo"/>.
///   3. Call <see cref="InsightDetailViewModel.Cleanup"/> on Unloaded to release
///      event subscriptions from the intelligence + narrative services.
///   4. Handle the back button — a Frame concern, not a ViewModel concern.
/// </summary>
public sealed partial class InsightDetailPage : Page
{
    public InsightDetailViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<InsightDetailViewModel>();

    public InsightDetailPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Cleanup();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string insightId)
            ViewModel.Load(insightId);
    }

    private void OnGoBackClick(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }
}
