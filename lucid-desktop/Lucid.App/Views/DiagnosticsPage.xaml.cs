using CommunityToolkit.Mvvm.DependencyInjection;
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

        _viewModel = Ioc.Default.GetRequiredService<DiagnosticsViewModel>();
        DataContext  = _viewModel;

        try
        {
            await _viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiagnosticsPage] OnNavigatedTo failed: {ex.Message}");
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _viewModel?.Cleanup();
    }
}
