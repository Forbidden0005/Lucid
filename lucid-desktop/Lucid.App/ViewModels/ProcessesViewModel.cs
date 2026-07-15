using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Execution;
using Lucid.Services.History;
using Lucid.Services.ProcessIntel;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ── Tab enum ──────────────────────────────────────────────────────────────────

public enum ProcessTab { All = 0, Anomalies = 1, ByCpu = 2, ByRam = 3 }

// ── Per-process row ViewModel ─────────────────────────────────────────────────

public sealed partial class ProcessRowViewModel : ObservableObject
{
    public ProcessRecord Record     { get; private set; }

    public int    Pid               => Record.ProcessId;
    public string Name              => Record.DisplayName;
    public string ProcessName       => Record.ProcessName;
    public string Cpu               => Record.CpuFormatted;
    public string Ram               => Record.RamFormatted;
    public string Threads           => Record.ThreadCount.ToString();
    public string Handles           => Record.HandleCount.ToString();
    public string Category          => Record.Category.ToString();
    public string Company           => Record.CompanyName;
    public string ExecutablePath    => Record.ExecutablePath;
    public string Uptime            => Record.UptimeFormatted;
    public bool   HasAnomalies      => Record.HasAnomalies;
    public string AnomalySummary    => Record.AnomalySummary;
    public bool   IsCritical        => Record.IsCritical;
    public bool   HasWindow         => Record.HasWindow;

    public Visibility AnomalyBadgeVisibility =>
        Record.HasAnomalies ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CriticalBadgeVisibility =>
        Record.IsCritical ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TerminateButtonVisibility =>
        !Record.IsCritical ? Visibility.Visible : Visibility.Collapsed;

    public ProcessRowViewModel(ProcessRecord record) => Record = record;

    public void Update(ProcessRecord record)
    {
        Record = record;
        OnPropertyChanged(nameof(Cpu));
        OnPropertyChanged(nameof(Ram));
        OnPropertyChanged(nameof(Threads));
        OnPropertyChanged(nameof(Handles));
        OnPropertyChanged(nameof(HasAnomalies));
        OnPropertyChanged(nameof(AnomalySummary));
        OnPropertyChanged(nameof(AnomalyBadgeVisibility));
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  ProcessesViewModel
// ═════════════════════════════════════════════════════════════════════════════

public sealed partial class ProcessesViewModel : ObservableObject
{
    // ── Tab state ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllTab), nameof(IsAnomaliesTab),
                               nameof(IsByCpuTab), nameof(IsByRamTab),
                               nameof(AllPanelVisibility), nameof(AnomaliesPanelVisibility),
                               nameof(ByCpuPanelVisibility), nameof(ByRamPanelVisibility))]
    private ProcessTab _activeTab = ProcessTab.All;

    public bool IsAllTab       => ActiveTab == ProcessTab.All;
    public bool IsAnomaliesTab => ActiveTab == ProcessTab.Anomalies;
    public bool IsByCpuTab     => ActiveTab == ProcessTab.ByCpu;
    public bool IsByRamTab     => ActiveTab == ProcessTab.ByRam;

    public Visibility AllPanelVisibility       => V(IsAllTab);
    public Visibility AnomaliesPanelVisibility => V(IsAnomaliesTab);
    public Visibility ByCpuPanelVisibility     => V(IsByCpuTab);
    public Visibility ByRamPanelVisibility     => V(IsByRamTab);

    // ── Summary state ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _processCount    = "—";
    [ObservableProperty] private string _anomalyCount    = "—";
    [ObservableProperty] private string _lastUpdated     = string.Empty;
    [ObservableProperty] private string _statusText      = string.Empty;

    public Visibility NoAnomaliesVisibility =>
        AllProcesses.Count > 0 && AnomalousProcesses.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AnomalyListVisibility =>
        AnomalousProcesses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    // ── Process lists ─────────────────────────────────────────────────────────

    public ObservableCollection<ProcessRowViewModel> AllProcesses       { get; } = new();
    public ObservableCollection<ProcessRowViewModel> AnomalousProcesses { get; } = new();
    public ObservableCollection<ProcessRowViewModel> TopByCpu           { get; } = new();
    public ObservableCollection<ProcessRowViewModel> TopByRam           { get; } = new();

    // Row cache keyed by PID — preserves IsExpanded state across ticks
    private readonly Dictionary<int, ProcessRowViewModel> _cache = new();

    private readonly ProcessIntelligenceService _processIntelligence;
    private readonly IActionExecutionEngine     _executionEngine;
    private readonly IOperationHistoryService   _historyService;

    private static Visibility V(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

    // ── Construction ──────────────────────────────────────────────────────────

    public ProcessesViewModel(
        ProcessIntelligenceService processIntelligence,
        IActionExecutionEngine     executionEngine,
        IOperationHistoryService   historyService)
    {
        _processIntelligence = processIntelligence;
        _executionEngine     = executionEngine;
        _historyService      = historyService;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Start()
    {
        _processIntelligence.SnapshotUpdated += OnSnapshotUpdated;

        var snap = _processIntelligence.LastSnapshot;
        if (snap is not null)
            OnSnapshotUpdated(this, snap);
    }

    public void Stop()
    {
        _processIntelligence.SnapshotUpdated -= OnSnapshotUpdated;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand] private void SelectTabAll()       => ActiveTab = ProcessTab.All;
    [RelayCommand] private void SelectTabAnomalies() => ActiveTab = ProcessTab.Anomalies;
    [RelayCommand] private void SelectTabByCpu()     => ActiveTab = ProcessTab.ByCpu;
    [RelayCommand] private void SelectTabByRam()     => ActiveTab = ProcessTab.ByRam;

    [RelayCommand]
    private async Task TerminateProcessAsync(ProcessRowViewModel row)
    {
        if (row is null || row.IsCritical) return;

        try
        {
            var log     = new ActionExecutionLog();
            var context = new ActionExecutionContext
            {
                IsElevated          = false,
                ConfirmationGranted = true,
                Log                 = log,
                Parameters = new Dictionary<string, string>
                {
                    [Services.Execution.Executors.TerminateProcessExecutor.ParamProcessId]
                        = row.Pid.ToString(),
                    [Services.Execution.Executors.TerminateProcessExecutor.ParamProcessName]
                        = row.ProcessName,
                },
            };

            var result = await _executionEngine
                .ExecuteAsync("action.process.terminate", context)
                .ConfigureAwait(true);

            StatusText = result.Message;

            if (result.IsSuccess)
            {
                RemoveFromAllLists(row.Pid);
                _cache.Remove(row.Pid);
                await RecordHistoryAsync(result, $"Terminate {row.Name}");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Terminate failed: {ex.Message}";
            Lucid.Services.Diagnostics.Logging.OperationalDiagnostics.ReportFailure("ProcessesVM", "TerminateProcessAsync failed", ex);
        }
    }

    [RelayCommand]
    private async Task OpenLocationAsync(ProcessRowViewModel row)
    {
        if (row is null) return;

        try
        {
            var log     = new ActionExecutionLog();
            var context = new ActionExecutionContext
            {
                IsElevated          = false,
                ConfirmationGranted = true,
                Log                 = log,
                Parameters = new Dictionary<string, string>
                {
                    [Services.Execution.Executors.OpenProcessLocationExecutor.ParamExecutablePath]
                        = row.ExecutablePath,
                    [Services.Execution.Executors.OpenProcessLocationExecutor.ParamProcessName]
                        = row.ProcessName,
                },
            };

            await _executionEngine
                .ExecuteAsync("action.process.open-location", context)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Lucid.Services.Diagnostics.Logging.OperationalDiagnostics.ReportFailure("ProcessesVM", "OpenLocationAsync failed", ex);
        }
    }

    // ── Snapshot handler ──────────────────────────────────────────────────────

    private void OnSnapshotUpdated(object? sender, ProcessIntelligenceSnapshot snap)
    {
        UpdateList(AllProcesses,       snap.All);
        UpdateList(AnomalousProcesses, snap.Anomalous);
        UpdateList(TopByCpu,           snap.TopByCpu);
        UpdateList(TopByRam,           snap.TopByRam);

        ProcessCount = $"{snap.All.Count} processes";
        AnomalyCount = snap.Anomalous.Count > 0
            ? $"{snap.Anomalous.Count} anomal{(snap.Anomalous.Count == 1 ? "y" : "ies")}"
            : "None";
        LastUpdated = snap.SnapshotAt.ToString("HH:mm:ss");
        StatusText  = string.Empty;

        OnPropertyChanged(nameof(NoAnomaliesVisibility));
        OnPropertyChanged(nameof(AnomalyListVisibility));
    }

    private void UpdateList(
        ObservableCollection<ProcessRowViewModel> collection,
        IReadOnlyList<ProcessRecord>              records)
    {
        // Update or add
        var seen = new HashSet<int>();
        foreach (var record in records)
        {
            seen.Add(record.ProcessId);
            if (_cache.TryGetValue(record.ProcessId, out var row))
            {
                row.Update(record);
                if (!collection.Contains(row))
                    collection.Add(row);
            }
            else
            {
                var newRow = new ProcessRowViewModel(record);
                _cache[record.ProcessId] = newRow;
                collection.Add(newRow);
            }
        }

        // Remove stale
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(collection[i].Pid))
                collection.RemoveAt(i);
        }
    }

    private void RemoveFromAllLists(int pid)
    {
        RemovePid(AllProcesses, pid);
        RemovePid(AnomalousProcesses, pid);
        RemovePid(TopByCpu, pid);
        RemovePid(TopByRam, pid);
    }

    private static void RemovePid(ObservableCollection<ProcessRowViewModel> col, int pid)
    {
        for (int i = col.Count - 1; i >= 0; i--)
            if (col[i].Pid == pid) col.RemoveAt(i);
    }

    // ── History ───────────────────────────────────────────────────────────────

    private async Task RecordHistoryAsync(ActionExecutionResult result, string title)
    {
        try
        {
            await _historyService.RecordAsync(new OperationRecord
            {
                ActionId        = result.ActionId,
                ActionTitle     = title,
                ExecutedAt      = result.ExecutedAt,
                DurationMs      = (long)result.Duration.TotalMilliseconds,
                Status          = result.Status.ToString(),
                IsSuccess       = result.IsSuccess,
                IsDryRun        = false,
                IsRollback      = false,
                Message         = result.Message,
                TotalLogEntries = result.Log.Count,
                Warnings        = result.Log.Where(e => e.Level == ActionLogLevel.Warning)
                    .Select(e => e.Message).ToList(),
                Errors          = result.Log.Where(e => e.Level == ActionLogLevel.Error)
                    .Select(e => e.Message).ToList(),
            });
        }
        catch (Exception ex)
        {
            Lucid.Services.Diagnostics.Logging.OperationalDiagnostics.ReportFailure("ProcessesVM", "RecordHistoryAsync failed", ex);
        }
    }
}
