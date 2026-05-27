using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

/// <summary>
/// Code-behind for the Operational Simulation ("What If?") page.
///
/// Follows the standard Lucid page pattern:
///   • ViewModel is created on NavigatedTo (receives AppServices reference).
///   • DataContext is cleared on NavigatedFrom.
///
/// The SimulationViewModel subscribes to intelligence services; Cleanup() is
/// called on NavigatedFrom to unsubscribe and cancel any in-flight simulation.
/// </summary>
public sealed partial class SimulationPage : Page
{
    private SimulationViewModel? _viewModel;

    public SimulationPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _viewModel  = new SimulationViewModel(
            AppServices.SimulationEngine,
            AppServices.SimulationHistory,
            AppServices.OutcomeVerification);
        DataContext = _viewModel;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        _viewModel?.Cleanup();
        _viewModel  = null;
        DataContext = null;
    }
}
