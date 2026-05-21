using ExplainMyPC.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ExplainMyPC.Views;

/// <summary>
/// Code-behind for the Operational Watchtower page.
/// Wires WatchtowerViewModel and handles navigation lifecycle.
/// </summary>
public sealed partial class WatchtowerPage : Page
{
    private WatchtowerViewModel? _viewModel;

    public WatchtowerPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _viewModel = new WatchtowerViewModel(
            AppServices.Watchtower,
            DispatcherQueue.GetForCurrentThread());

        DataContext = _viewModel;

        // Trigger initial refresh if no snapshot exists yet
        if (AppServices.Watchtower.LastSnapshot is null)
        {
            await _viewModel.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _viewModel?.Cleanup();
    }
}
