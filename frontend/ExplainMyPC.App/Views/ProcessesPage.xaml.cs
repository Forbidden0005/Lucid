using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Views;

public sealed partial class ProcessesPage : Page
{
    public ProcessesViewModel ViewModel { get; } = new ProcessesViewModel();

    public ProcessesPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Stop();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => ViewModel.Start();

    private void Terminate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProcessRowViewModel row })
            _ = ViewModel.TerminateProcessCommand.ExecuteAsync(row);
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProcessRowViewModel row })
            _ = ViewModel.OpenLocationCommand.ExecuteAsync(row);
    }
}
