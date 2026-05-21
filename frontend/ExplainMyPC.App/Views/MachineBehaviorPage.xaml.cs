using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ExplainMyPC.Views;

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

        _viewModel  = new MachineBehaviorViewModel(
            AppServices.BehavioralContext,
            AppServices.WorkloadProfiling);
        DataContext = _viewModel;

        await _viewModel.InitializeAsync().ConfigureAwait(true);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _viewModel?.Cleanup();
    }
}
