using Lucid.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Views;

public sealed partial class SecurityPage : Page
{
    public SecurityViewModel ViewModel { get; } = new SecurityViewModel();

    public SecurityPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Cleanup();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) { }

    private void OpenVirusTotal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SecurityFindingViewModel vm })
            _ = ViewModel.OpenVirusTotalCommand.ExecuteAsync(vm);
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SecurityFindingViewModel vm })
            _ = ViewModel.OpenFileLocationCommand.ExecuteAsync(vm);
    }
}
