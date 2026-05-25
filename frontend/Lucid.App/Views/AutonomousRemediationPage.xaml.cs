using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

/// <summary>
/// Code-behind for the Autonomous Remediation Planner page.
///
/// Follows the standard Lucid page pattern:
///   • ViewModel is created on NavigatedTo (receives AppServices reference).
///   • ViewModel.Cleanup() is called on NavigatedFrom to unsubscribe events.
/// </summary>
public sealed partial class AutonomousRemediationPage : Page
{
    private AutonomousRemediationViewModel? _viewModel;

    public AutonomousRemediationPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _viewModel     = new AutonomousRemediationViewModel(AppServices.RemediationService);
        DataContext    = _viewModel;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        _viewModel?.Cleanup();
        _viewModel  = null;
        DataContext  = null;
    }
}
