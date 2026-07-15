using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Views;

/// <summary>
/// Apps page — installed applications and startup impact.
/// </summary>
public sealed partial class AppsPage : Page
{
    // Migrated off the AppServices static locator (ROADMAP C4 template):
    // dependencies are resolved from the service registry via App.GetService.
    public AppsViewModel ViewModel { get; } =
        new AppsViewModel(App.GetService<Services.Startup.IStartupManagementService>());

    public AppsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
