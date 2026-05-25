using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lucid.Views;

/// <summary>
/// Historical Intelligence page — surfaces long-term operational patterns,
/// health scores, metric trends, and recurring issue summaries built from
/// the local SQLite operational database.
///
/// All data is computed locally and deterministically. No cloud, no ML.
/// </summary>
public sealed partial class HistoricalPage : Page
{
    public HistoricalViewModel ViewModel { get; }

    public HistoricalPage()
    {
        InitializeComponent();
        ViewModel   = new HistoricalViewModel(AppServices.HistoricalAnalytics);
        DataContext = ViewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.InitializeAsync();
    }
}
