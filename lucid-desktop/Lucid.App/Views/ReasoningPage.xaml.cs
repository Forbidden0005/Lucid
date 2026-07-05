using CommunityToolkit.Mvvm.DependencyInjection;
using Lucid.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

/// <summary>
/// Code-behind for the Reasoning page.
/// Surfaces the Phase 19–21 cognitive intelligence layers:
/// workload context, active inferences, recommendations, patterns,
/// baselines, and calibration health.
/// </summary>
public sealed partial class ReasoningPage : Page
{
    private ReasoningPageViewModel? _viewModel;

    public ReasoningPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _viewModel = Ioc.Default.GetRequiredService<ReasoningPageViewModel>();

        DataContext = _viewModel;

        try
        {
            await _viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReasoningPage] OnNavigatedTo failed: {ex.Message}");
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
    }
}
