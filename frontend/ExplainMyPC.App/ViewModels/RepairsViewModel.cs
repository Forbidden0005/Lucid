using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Core.MVVM;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for the Repairs page.
///
/// Phase 4: RepairService will expose:
///   • SFC (System File Checker) integration
///   • DISM health restore
///   • Networking stack repair
///   • Windows Update repair
///
/// CRITICAL (per security-model.md and CLAUDE.md): Every repair operation
/// must follow the 7-step safe workflow BEFORE execution:
///   1. Explain what will change
///   2. Estimate risk
///   3. Create restore point
///   4. Create rollback snapshot
///   5. Execute
///   6. Verify outcome
///   7. Allow rollback
///
/// No destructive action may execute without user confirmation.
/// </summary>
public partial class RepairsViewModel : ViewModelBase
{
    [ObservableProperty] private bool   _repairInProgress = false;
    [ObservableProperty] private string _repairSummary    =
        "Choose a repair tool below. A restore point is always created before any changes are made.";
    [ObservableProperty] private string _lastRepairResult = string.Empty;

    [RelayCommand]
    private async Task RunSfcAsync()
    {
        // Phase 4: RepairService.RunSfcAsync() — with restore point gate.
        IsBusy        = true;
        StatusMessage = "SFC integration available in Phase 4.";
        await Task.Delay(300);
        IsBusy = false;
    }

    [RelayCommand]
    private async Task RunDismAsync()
    {
        IsBusy        = true;
        StatusMessage = "DISM integration available in Phase 4.";
        await Task.Delay(300);
        IsBusy = false;
    }
}
