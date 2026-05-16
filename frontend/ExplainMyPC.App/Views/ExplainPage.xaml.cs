using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Views;

/// <summary>
/// Explain My PC — the flagship intelligence page.
/// Code-behind resolves the ViewModel and keeps all logic in ExplainViewModel.
/// </summary>
public sealed partial class ExplainPage : Page
{
    public ExplainViewModel ViewModel { get; }

    public ExplainPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<ExplainViewModel>();
    }
}
