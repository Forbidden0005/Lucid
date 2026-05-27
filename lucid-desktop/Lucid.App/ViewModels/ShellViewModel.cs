using CommunityToolkit.Mvvm.ComponentModel;
using Lucid.Core.MVVM;
using Lucid.Core.Navigation;

namespace Lucid.ViewModels;

/// <summary>
/// ViewModel for the main shell window (MainWindow).
///
/// Owns shell-level state that survives page navigations:
///   • Current page title (for future top-bar binding)
///   • Back-navigation coordination
///
/// Registered as a singleton in DI because MainWindow is the only consumer
/// and it lives for the full application lifetime.
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string _currentPageTitle = "Dashboard";

    public ShellViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public bool CanGoBack => _navigationService.CanGoBack;

    public void NavigateTo(string pageKey) =>
        _navigationService.Navigate(pageKey);

    public void GoBack()
    {
        if (_navigationService.CanGoBack)
            _navigationService.GoBack();
    }
}
