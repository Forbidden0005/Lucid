using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Views;

public sealed partial class StoragePage : Page
{
    public StorageViewModel ViewModel { get; }

    public StoragePage()
    {
        InitializeComponent();
        ViewModel = App.GetService<StorageViewModel>();
    }
}
