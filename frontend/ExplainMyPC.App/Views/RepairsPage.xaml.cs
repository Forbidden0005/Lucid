using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Views;

public sealed partial class RepairsPage : Page
{
    public RepairsViewModel ViewModel { get; }

    public RepairsPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<RepairsViewModel>();
    }
}
