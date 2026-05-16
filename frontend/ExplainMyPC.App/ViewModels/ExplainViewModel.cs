using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Core.MVVM;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for the Explain My PC page — the flagship intelligence feature.
///
/// Phase 3 will wire this to the Intelligence Engine, which runs the
/// rules engine + correlation engine and produces natural-language summaries.
///
/// Output philosophy (intelligence-engine.md):
///   GOOD: "Several startup apps are slowing your boot time."
///   BAD:  "Boot degradation threshold exceeded."
/// </summary>
public partial class ExplainViewModel : ViewModelBase
{
    [ObservableProperty] private bool   _hasScanResults = false;
    [ObservableProperty] private string _summary        =
        "Run a scan to get a plain-language explanation of your system's health.";

    [ObservableProperty] private string _lastScanTime = "Never";

    [RelayCommand]
    private async Task RunExplainScanAsync()
    {
        IsBusy  = true;
        Summary = "Analyzing your system…";

        // Phase 3: await IntelligenceEngine.AnalyzeAsync()
        await Task.Delay(500); // placeholder

        IsBusy         = false;
        HasScanResults = false;
        Summary        = "Run a scan to get a plain-language explanation of your system's health.";
        StatusMessage  = "Full analysis available in Phase 3.";
    }
}
