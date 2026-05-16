using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Core.MVVM;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for the Security page.
///
/// Phase 2: SecurityService will scan for:
///   • Unsigned startup executables
///   • Suspicious persistence entries
///   • Scheduled task anomalies
///   • Hosts file modifications
///   • DNS tampering indicators
///
/// Per security-model.md, findings are presented with confidence scores
/// and evidence — never as fear-based alarmist warnings.
/// </summary>
public partial class SecurityViewModel : ViewModelBase
{
    [ObservableProperty] private int    _threatCount        = 0;
    [ObservableProperty] private int    _warningCount       = 0;
    [ObservableProperty] private string _securitySummary    =
        "No major security concerns detected. Run a scan to check for suspicious activity.";
    [ObservableProperty] private bool   _hasScanResults     = false;

    [RelayCommand]
    private async Task RunSecurityScanAsync()
    {
        IsBusy        = true;
        StatusMessage = "Scanning…";
        await Task.Delay(300); // Phase 2: real scan
        IsBusy        = false;
        StatusMessage = "Security scanning available in Phase 2.";
    }
}
