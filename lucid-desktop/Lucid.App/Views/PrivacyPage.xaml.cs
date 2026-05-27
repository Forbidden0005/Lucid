using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Views;

/// <summary>
/// Privacy page — reads the Windows ConsentStore registry and displays
/// per-permission categories with per-app grant records.
/// </summary>
public sealed partial class PrivacyPage : Page
{
    public PrivacyViewModel ViewModel { get; } = new PrivacyViewModel();

    public PrivacyPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
