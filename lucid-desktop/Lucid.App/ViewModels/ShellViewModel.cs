using CommunityToolkit.Mvvm.ComponentModel;

namespace Lucid.ViewModels;

/// <summary>
/// Compile-safe shell state holder retained for future navigation refactoring.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string _currentPageTitle = "Dashboard";
}
