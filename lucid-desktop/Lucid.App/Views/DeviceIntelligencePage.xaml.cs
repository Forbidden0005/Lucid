using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

/// <summary>
/// Code-behind for the Device Intelligence page.
/// Wires DeviceIntelligenceViewModel and handles navigation lifecycle.
/// </summary>
public sealed partial class DeviceIntelligencePage : Page
{
    private DeviceIntelligenceViewModel? _viewModel;

    public DeviceIntelligencePage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _viewModel = new DeviceIntelligenceViewModel(
            AppServices.DeviceIdentity,
            AppServices.TrustedDevices,
            AppServices.LocalSync,
            AppServices.DistributedTimeline,
            AppServices.CrossMachineAnalytics);

        DataContext = _viewModel;

        try
        {
            await _viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Lucid.Services.Diagnostics.Logging.OperationalDiagnostics.ReportFailure("DeviceIntelligencePage", "OnNavigatedTo failed", ex);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _viewModel?.Cleanup();
    }
}
