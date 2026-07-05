using CommunityToolkit.Mvvm.DependencyInjection;
using Lucid.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

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

        _viewModel = Ioc.Default.GetRequiredService<WatchtowerViewModel>();

        DataContext = _viewModel;

        // Trigger initial refresh if no snapshot exists yet
        try
        {
            if (AppServices.Watchtower.LastSnapshot is null)
            {
                await _viewModel.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WatchtowerPage] OnNavigatedTo failed: {ex.Message}");
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _viewModel?.Cleanup();
    }
}
