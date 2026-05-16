using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Views;

public sealed partial class ProcessesPage : Page
{
    public ProcessesViewModel ViewModel { get; }

    public ProcessesPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<ProcessesViewModel>();
    }
}
