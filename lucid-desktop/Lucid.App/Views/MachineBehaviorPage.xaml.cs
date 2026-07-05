using CommunityToolkit.Mvvm.DependencyInjection;
using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

/// <summary>
/// Code-behind for the Machine Behavior page.
/// Wires MachineBehaviorViewModel and handles navigation lifecycle.
/// </summary>
public sealed partial class MachineBehaviorPage : Page
{
    private MachineBehaviorViewModel? _viewModel;

    public MachineBehaviorPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _viewModel  = Ioc.Default.GetRequiredService<MachineBehaviorViewModel>();
        DataContext = _viewModel;

        try
        {
            await _viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MachineBehaviorPage] OnNavigatedTo failed: {ex.Message}");
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _viewModel?.Cleanup();
    }
}
