using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

/// <summary>
/// Code-behind for the Internal Diagnostics page.
/// Wires the DiagnosticsViewModel to the page and handles navigation lifecycle.
/// </summary>
public sealed partial class DiagnosticsPage : Page
{
    private DiagnosticsViewModel? _viewModel;

    public DiagnosticsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _viewModel = new DiagnosticsViewModel(AppServices.Diagnostics);
        DataContext  = _viewModel;

        try
        {
            await _viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Lucid.Services.Diagnostics.Logging.OperationalDiagnostics.ReportFailure("DiagnosticsPage", "OnNavigatedTo failed", ex);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _viewModel?.Cleanup();
    }
}
