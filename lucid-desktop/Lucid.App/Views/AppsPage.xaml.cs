using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Views;

/// <summary>
/// Apps page — installed applications and startup impact.
/// </summary>
public sealed partial class AppsPage : Page
{
    public AppsViewModel ViewModel { get; } = new AppsViewModel(AppServices.StartupManagement);

    public AppsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
