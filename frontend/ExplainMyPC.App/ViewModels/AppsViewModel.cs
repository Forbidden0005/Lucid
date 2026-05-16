using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Core.MVVM;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for the Apps page.
///
/// Phase 2: ScanService will enumerate installed applications and
///   startup entries, annotating each with:
///   • Startup impact (None / Low / Medium / High)
///   • Publisher / signature status
///   • Last-used estimate
///
/// Uninstall actions (Phase 4) require a restore point before execution.
/// </summary>
public partial class AppsViewModel : ViewModelBase
{
    [ObservableProperty] private int    _installedAppCount  = 0;
    [ObservableProperty] private int    _startupAppCount    = 0;
    [ObservableProperty] private string _appsSummary        =
        "Scan to see installed applications and their startup impact.";

    [RelayCommand]
    private async Task LoadAppsAsync()
    {
        IsBusy        = true;
        StatusMessage = "Loading…";
        await Task.Delay(300);
        IsBusy        = false;
        StatusMessage = "App scanning available in Phase 2.";
    }
}
