using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Views;

public sealed partial class PrivacyPage : Page
{
    public PrivacyViewModel ViewModel { get; }

    public PrivacyPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<PrivacyViewModel>();
    }
}
