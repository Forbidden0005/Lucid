using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

/// <summary>
/// Runtime Governance page — shows live workload concurrency, throttling decisions,
/// and adaptive polling state from <see cref="AppServices.Governance"/>.
/// </summary>
public sealed partial class RuntimeGovernancePage : Page
{
    public RuntimeGovernanceViewModel ViewModel { get; }

    public RuntimeGovernancePage()
    {
        InitializeComponent();
        ViewModel   = new RuntimeGovernanceViewModel(AppServices.Governance);
        DataContext = ViewModel;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Lucid.Services.Diagnostics.Logging.OperationalDiagnostics.ReportFailure("RuntimeGovernancePage", "OnNavigatedTo failed", ex);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Cleanup();
    }
}
