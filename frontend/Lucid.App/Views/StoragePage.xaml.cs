using Lucid.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Views;

/// <summary>
/// Storage Intelligence page.
/// Code-behind: thin — wires ViewModel and Cleanup only.
/// </summary>
public sealed partial class StoragePage : Page
{
    public StorageViewModel ViewModel { get; } = new StorageViewModel();

    public StoragePage()
    {
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Cleanup();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Nothing to wire via callback here — storage commands are self-contained.
    }
}
