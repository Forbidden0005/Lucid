using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Views;

/// <summary>
/// Dashboard — the first page users land on.
///
/// Code-behind is kept minimal: it resolves the ViewModel from DI and
/// exposes it as a property so x:Bind in the XAML can compile against it.
/// No business logic lives here.
///
/// Unloaded calls ViewModel.Cleanup() to unsubscribe from the singleton
/// ITelemetryService, preventing the service from holding a strong reference
/// to this transient ViewModel after navigation away.
/// </summary>
public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<DashboardViewModel>();
        Unloaded += (_, _) => ViewModel.Cleanup();
    }
}
