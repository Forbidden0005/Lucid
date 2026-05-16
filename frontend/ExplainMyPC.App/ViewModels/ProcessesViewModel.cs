using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExplainMyPC.Core.MVVM;

namespace ExplainMyPC.ViewModels;

/// <summary>
/// ViewModel for the Processes page.
///
/// Phase 2: TelemetryService will push live process list with
///   CPU%, RAM bytes, disk I/O, process tree, and startup impact.
///
/// Polling rates (architecture.md): CPU ~1 s, RAM ~1 s, Disk ~2 s.
/// The service is responsible for throttling — this VM just exposes state.
/// </summary>
public partial class ProcessesViewModel : ViewModelBase
{
    [ObservableProperty] private int    _totalProcessCount  = 0;
    [ObservableProperty] private double _systemCpuPercent   = 0;
    [ObservableProperty] private double _systemRamPercent   = 0;
    [ObservableProperty] private string _processSummary     =
        "Start monitoring to see live process information.";

    [RelayCommand]
    private Task StartMonitoringAsync()
    {
        // Phase 2: subscribe to TelemetryService.ProcessUpdated event.
        StatusMessage = "Live process monitoring available in Phase 2.";
        return Task.CompletedTask;
    }
}
