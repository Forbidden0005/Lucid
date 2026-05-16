using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Core.MVVM;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for the Storage page.
///
/// Phase 2: Rust storage engine will provide:
///   • Junk file accumulation totals
///   • Duplicate file analysis
///   • Large file discovery
///   • SSD/HDD SMART health status
///
/// Per storage-model (CLAUDE.md): cleanup is conservative and reversible.
/// Never auto-delete. Always explain what will be removed and why.
/// </summary>
public partial class StorageViewModel : ViewModelBase
{
    [ObservableProperty] private long   _totalDiskBytes      = 0;
    [ObservableProperty] private long   _usedDiskBytes       = 0;
    [ObservableProperty] private long   _reclaimableBytes    = 0;
    [ObservableProperty] private string _diskHealthSummary   =
        "Run a storage scan to analyse disk health and find reclaimable space.";
    [ObservableProperty] private string _smartStatus         = "Unknown";

    [RelayCommand]
    private async Task RunStorageScanAsync()
    {
        IsBusy        = true;
        StatusMessage = "Scanning…";
        await Task.Delay(300);
        IsBusy        = false;
        StatusMessage = "Storage scanning available in Phase 2.";
    }
}
