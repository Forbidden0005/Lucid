using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Views;

public sealed partial class SecurityPage : Page
{
    public SecurityViewModel ViewModel { get; }

    public SecurityPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<SecurityViewModel>();
    }
}
