using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Validation;
using Lucid.Services.Governance;
using Lucid.Services.History;
using Lucid.Services.Storage;
using Lucid.Services.Timeline;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ── Tab enum ──────────────────────────────────────────────────────────────────

public enum StorageTab { Overview = 0, LargeFiles = 1, Duplicates = 2, Downloads = 3 }

// ── Large file row ViewModel ─────────────────────────────────────────────────

public sealed class LargeFileViewModel
{
    public LargeFileRecord Record      { get; }
    public string          Name        => Record.FileName;
    public string          Path        => Record.DirectoryPath;
    public string          Size        => Record.SizeFormatted;
    public string          Age         => Record.AgeDescription;
    public string          Category    => Record.Category.ToString();

    public LargeFileViewModel(LargeFileRecord r) => Record = r;
}

// ── Near-duplicate pair row ViewModel ────────────────────────────────────────

/// <summary>
/// A heuristic near-duplicate pair (similar names, copy patterns, or format
/// variants). Review-only: these files are not byte-identical, so no delete
/// command is exposed — the user judges each pair themselves.
/// </summary>
public sealed class NearDuplicateViewModel
{
    public NearDuplicateMatch Match { get; }

    public string NameA      => Match.FileA.FileName;
    public string NameB      => Match.FileB.FileName;
    public string PathA      => Match.FileA.DirectoryPath;
    public string PathB      => Match.FileB.DirectoryPath;
    public string SizeA      => Match.FileA.SizeFormatted;
    public string SizeB      => Match.FileB.SizeFormatted;
    public string Reason     => Match.MatchReason;
    public string Confidence => Match.ConfidenceFormatted;
    public string Redundant  => Match.RedundantFormatted;

    public NearDuplicateViewModel(NearDuplicateMatch m) => Match = m;
}

// ── Duplicate group row ViewModel ────────────────────────────────────────────

public sealed class DuplicateGroupViewModel
{
    public DuplicateFileGroup Group    { get; }
    public string Count               => $"{Group.Count} copies";
    public string Waste               => Group.WasteFormatted;
    public string KeepName            => Group.KeepCandidate.FileName;
    public string KeepPath            => Group.KeepCandidate.DirectoryPath;
    public string HashShort           => Group.Hash[..8] + "…";

    public IReadOnlyList<LargeFileViewModel> Files =>
        Group.Files.Select(f => new LargeFileViewModel(f)).ToList();

    /// <summary>
    /// True when at least one file in the group sits in a protected location,
    /// so the delete executor would refuse the whole group. Such groups belong
    /// in the "review manually" section, never in the actionable list with a
    /// live delete button.
    /// </summary>
    public bool   IsProtected      { get; }

    /// <summary>Plain-English reason the group is protected (empty when actionable).</summary>
    public string ProtectionReason { get; }

    public DuplicateGroupViewModel(DuplicateFileGroup g)
    {
        Group = g;
        // Same policy the delete executor enforces, so a group shown in the
        // actionable list is never one the executor would refuse.
        ProtectionReason = DuplicateProtectionPolicy.FindBlockedReason(
            g.KeepCandidate.FullPath,
            g.DeleteCandidates.Select(f => f.FullPath)) ?? string.Empty;
        IsProtected = ProtectionReason.Length > 0;
    }
}

// ── Category heatmap row ─────────────────────────────────────────────────────

public sealed class CategoryRowViewModel
{
    public StorageCategorySnapshot Snapshot   { get; }
    public string Label                       => Snapshot.Label;
    public string Size                        => Snapshot.SizeFormatted;
    public string FileCount                   => $"{Snapshot.FileCount:N0} files";
    public double WidthPercent                { get; }

    public CategoryRowViewModel(StorageCategorySnapshot s, long maxBytes)
    {
        Snapshot     = s;
        WidthPercent = maxBytes > 0
            ? Math.Clamp(s.TotalBytes / (double)maxBytes * 100.0, 1, 100) : 0;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  StorageViewModel
// ═════════════════════════════════════════════════════════════════════════════

public sealed partial class StorageViewModel : ObservableObject
{
    private readonly DispatcherQueue             _dispatcher;
    private readonly ITimelineAggregationService _timeline;
    private readonly IActionExecutionEngine      _executionEngine;
    private readonly IOperationHistoryService    _historyService;
    private readonly IRuntimeGovernanceService?  _governance;

    // ── Scan state ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(CancelButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(ProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))] // EmptyStateVisibility = !HasResults && !IsScanning
    private bool _isScanning;

    [ObservableProperty] private int    _scanPercent;
    [ObservableProperty] private string _scanPhase    = string.Empty;
    [ObservableProperty] private string _scanSubtext  = string.Empty;

    // ── Result state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelVisibility))]   // OverviewPanelVisibility = IsOverviewTab && HasResults
    [NotifyPropertyChangedFor(nameof(LargeFilesPanelVisibility))] // LargeFilesPanelVisibility = IsLargeFilesTab && HasResults
    [NotifyPropertyChangedFor(nameof(DuplicatesPanelVisibility))] // DuplicatesPanelVisibility = IsDuplicatesTab && HasResults
    private bool _hasResults;

    [ObservableProperty] private string _totalScanned      = string.Empty;
    [ObservableProperty] private string _largeFilesSummary = string.Empty;
    [ObservableProperty] private string _wasteSummary      = string.Empty;
    [ObservableProperty] private string _scanDuration      = string.Empty;

    // Duplicate-tab section state (set after each scan completes).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionableDuplicatesVisibility))]
    [NotifyPropertyChangedFor(nameof(NoActionableDuplicatesVisibility))]
    private bool _hasActionableDuplicates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProtectedDuplicatesVisibility))]
    private bool _hasProtectedDuplicates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NearDuplicatesVisibility))]
    private bool _hasNearDuplicates;

    // ── Tab state ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewTab))]
    [NotifyPropertyChangedFor(nameof(IsLargeFilesTab))]
    [NotifyPropertyChangedFor(nameof(IsDuplicatesTab))]
    [NotifyPropertyChangedFor(nameof(IsDownloadsTab))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(LargeFilesPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(DuplicatesPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(DownloadsPanelVisibility))]
    private StorageTab _activeTab = StorageTab.Overview;

    public bool IsOverviewTab   => ActiveTab == StorageTab.Overview;
    public bool IsLargeFilesTab => ActiveTab == StorageTab.LargeFiles;
    public bool IsDuplicatesTab => ActiveTab == StorageTab.Duplicates;
    public bool IsDownloadsTab  => ActiveTab == StorageTab.Downloads;

    // ── Visibility helpers ────────────────────────────────────────────────────

    public Visibility ScanButtonVisibility    => V(!IsScanning);
    public Visibility CancelButtonVisibility  => V(IsScanning);
    public Visibility ProgressVisibility      => V(IsScanning);
    public Visibility ResultsVisibility       => V(HasResults);
    public Visibility EmptyStateVisibility    => V(!HasResults && !IsScanning);
    public Visibility OverviewPanelVisibility   => V(IsOverviewTab && HasResults);
    public Visibility LargeFilesPanelVisibility => V(IsLargeFilesTab && HasResults);
    public Visibility DuplicatesPanelVisibility => V(IsDuplicatesTab && HasResults);
    public Visibility DownloadsPanelVisibility  => V(IsDownloadsTab);

    // Within the Duplicates panel: the actionable list, the "nothing to act on"
    // note, and the protected-locations section are shown independently.
    public Visibility ActionableDuplicatesVisibility   => V(HasActionableDuplicates);
    public Visibility NoActionableDuplicatesVisibility => V(!HasActionableDuplicates);
    public Visibility ProtectedDuplicatesVisibility    => V(HasProtectedDuplicates);
    public Visibility NearDuplicatesVisibility         => V(HasNearDuplicates);

    private static Visibility V(bool show) =>
        show ? Visibility.Visible : Visibility.Collapsed;

    // ── Result collections ────────────────────────────────────────────────────

    public ObservableCollection<LargeFileViewModel>     LargeFiles      { get; } = new();

    /// <summary>Actionable duplicate groups — safe to delete, shown in the main list.</summary>
    public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = new();

    /// <summary>
    /// Duplicate groups in protected locations. Surfaced in a separate
    /// "review manually" section so the main list only holds cases that need
    /// (and can take) action.
    /// </summary>
    public ObservableCollection<DuplicateGroupViewModel> ProtectedDuplicateGroups { get; } = new();

    /// <summary>Heuristic near-duplicate pairs — review-only, never actionable.</summary>
    public ObservableCollection<NearDuplicateViewModel> NearDuplicates { get; } = new();

    public ObservableCollection<CategoryRowViewModel>    Categories      { get; } = new();
    public ObservableCollection<LargeFileViewModel>      OldDownloads    { get; } = new();

    // ── Scan service ──────────────────────────────────────────────────────────

    private CancellationTokenSource? _cts;

    // ── Status text ───────────────────────────────────────────────────────────

    [ObservableProperty] private string _statusText = string.Empty;

    // ── Construction ──────────────────────────────────────────────────────────

    public StorageViewModel(
        ITimelineAggregationService timeline,
        IActionExecutionEngine      executionEngine,
        IOperationHistoryService    historyService,
        IRuntimeGovernanceService?  governance = null)
    {
        _timeline        = timeline;
        _executionEngine = executionEngine;
        _historyService  = historyService;
        _governance      = governance;
        _dispatcher      = DispatcherQueue.GetForCurrentThread()
                        ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;

        _cts      = new CancellationTokenSource();
        IsScanning = true;
        HasResults = false;
        ScanPercent = 0;
        ScanPhase   = "Preparing…";
        ScanSubtext = string.Empty;
        StatusText  = string.Empty;
        LargeFiles.Clear();
        DuplicateGroups.Clear();
        ProtectedDuplicateGroups.Clear();
        NearDuplicates.Clear();
        Categories.Clear();
        OldDownloads.Clear();

        var timeline = _timeline as Services.Timeline.TimelineAggregationService;
        var svc      = new StorageAnalysisService(_dispatcher, timeline, _governance);

        svc.ScanProgressChanged += OnProgress;
        svc.ScanCompleted       += OnCompleted;

        try
        {
            await svc.StartScanAsync(_cts.Token);
        }
        finally
        {
            svc.ScanProgressChanged -= OnProgress;
            svc.ScanCompleted       -= OnCompleted;
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelScan() => _cts?.Cancel();

    [RelayCommand]
    private void SelectTabOverview()   => ActiveTab = StorageTab.Overview;
    [RelayCommand]
    private void SelectTabLargeFiles() => ActiveTab = StorageTab.LargeFiles;
    [RelayCommand]
    private void SelectTabDuplicates() => ActiveTab = StorageTab.Duplicates;
    [RelayCommand]
    private void SelectTabDownloads()  => ActiveTab = StorageTab.Downloads;

    /// <summary>Delete a single large file via the execution engine (with staging).</summary>
    [RelayCommand]
    private async Task DeleteLargeFileAsync(LargeFileViewModel file)
    {
        if (file is null) return;

        try
        {
            var log     = new ActionExecutionLog();
            var context = new ActionExecutionContext
            {
                IsElevated          = false,
                ConfirmationGranted = true,  // confirmed by the UI button
                Log                 = log,
                Parameters          = new Dictionary<string, string>
                {
                    [Services.Execution.Executors.DeleteLargeFileExecutor.ParamFilePath]
                        = file.Record.FullPath,
                },
            };

            var result = await _executionEngine
                .ExecuteAsync("action.storage.delete-large-file", context)
                .ConfigureAwait(true);

            StatusText = result.Message;

            if (result.IsSuccess)
            {
                LargeFiles.Remove(file);
                await RecordHistoryAsync(result, "Delete Large File");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Delete failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[StorageVM] DeleteLargeFileAsync failed: {ex}");
        }
    }

    /// <summary>Delete all non-keep duplicates in a group.</summary>
    [RelayCommand]
    private async Task DeleteDuplicatesAsync(DuplicateGroupViewModel group)
    {
        if (group is null) return;

        try
        {
            var paths = string.Join('|',
                group.Group.DeleteCandidates.Select(f => f.FullPath));

            var log     = new ActionExecutionLog();
            var context = new ActionExecutionContext
            {
                IsElevated          = false,
                ConfirmationGranted = true,
                Log                 = log,
                Parameters          = new Dictionary<string, string>
                {
                    [Services.Execution.Executors.DeleteDuplicateGroupExecutor.ParamKeepPath]
                        = group.Group.KeepCandidate.FullPath,
                    [Services.Execution.Executors.DeleteDuplicateGroupExecutor.ParamDeletePaths]
                        = paths,
                },
            };

            var result = await _executionEngine
                .ExecuteAsync("action.storage.delete-duplicate-group", context)
                .ConfigureAwait(true);

            StatusText = result.Message;

            if (result.IsSuccess || result.Status == ActionExecutionStatus.PartialSuccess)
            {
                DuplicateGroups.Remove(group);
                await RecordHistoryAsync(result, "Delete Duplicates");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Delete failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[StorageVM] DeleteDuplicatesAsync failed: {ex}");
        }
    }

    /// <summary>
    /// Opens the containing folder for a protected group's kept file in
    /// Explorer (with the file selected) so the user can review or remove it
    /// manually. Non-destructive — Lucid never touches protected locations
    /// itself.
    /// </summary>
    [RelayCommand]
    private void OpenGroupLocation(DuplicateGroupViewModel? group)
    {
        if (group is null) return;

        var target = group.Group.KeepCandidate.FullPath;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "explorer.exe",
                Arguments       = $"/select,\"{target}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't open location: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[StorageVM] OpenGroupLocation failed: {ex}");
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnProgress(object? sender, StorageScanProgress p)
    {
        ScanPercent = p.PercentComplete;
        ScanPhase   = p.Phase;
        ScanSubtext =
            $"{p.FilesProcessed:N0} files · " +
            $"{StorageFormatHelper.FormatBytes(p.BytesProcessed)}";
    }

    private void OnCompleted(object? sender, StorageAnalysisResult result)
    {
        if (result.WasCancelled)
        {
            StatusText = "Scan cancelled.";
            return;
        }

        // Populate large files
        LargeFiles.Clear();
        foreach (var f in result.LargeFiles)
            LargeFiles.Add(new LargeFileViewModel(f));

        // Populate duplicates — actionable groups go in the main list; groups
        // in protected locations go to the "review manually" section so the
        // main list only holds cases that need (and can take) action.
        DuplicateGroups.Clear();
        ProtectedDuplicateGroups.Clear();
        foreach (var g in result.DuplicateGroups)
        {
            var vm = new DuplicateGroupViewModel(g);
            if (vm.IsProtected) ProtectedDuplicateGroups.Add(vm);
            else                DuplicateGroups.Add(vm);
        }
        HasActionableDuplicates = DuplicateGroups.Count > 0;
        HasProtectedDuplicates  = ProtectedDuplicateGroups.Count > 0;

        // Populate near-duplicate pairs (review-only)
        NearDuplicates.Clear();
        foreach (var m in result.NearDuplicates)
            NearDuplicates.Add(new NearDuplicateViewModel(m));
        HasNearDuplicates = NearDuplicates.Count > 0;

        // Populate categories
        Categories.Clear();
        long maxBytes = result.CategoryBreakdown.Count > 0
            ? result.CategoryBreakdown[0].TotalBytes : 1;
        foreach (var cat in result.CategoryBreakdown)
            Categories.Add(new CategoryRowViewModel(cat, maxBytes));

        // Populate old downloads (>= 90 days, in Downloads folder)
        OldDownloads.Clear();
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var dlPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                     + @"\Downloads";
        foreach (var f in result.LargeFiles
                     .Where(f => f.LastModifiedUtc < cutoff &&
                                 f.FullPath.StartsWith(dlPath,
                                     StringComparison.OrdinalIgnoreCase))
                     .Take(50))
        {
            OldDownloads.Add(new LargeFileViewModel(f));
        }

        // Summary stats
        TotalScanned      =
            $"{result.TotalFilesScanned:N0} files · " +
            $"{StorageFormatHelper.FormatBytes(result.TotalBytesScanned)}";
        LargeFilesSummary =
            $"{result.LargeFiles.Count} files · {result.LargeFilesFormatted}";
        WasteSummary      =
            $"{result.DuplicateGroups.Count} groups · {result.WasteFormatted}";
        ScanDuration      =
            result.Duration.TotalMinutes >= 1
                ? $"{result.Duration.TotalMinutes:F1} min"
                : $"{result.Duration.TotalSeconds:F0} sec";

        HasResults = true;
        ScanPhase  = "Scan complete";
        StatusText = $"Scanned {result.TotalFilesScanned:N0} files in {ScanDuration}.";
    }

    // ── History recording ─────────────────────────────────────────────────────

    private async Task RecordHistoryAsync(
        ActionExecutionResult result, string title)
    {
        try
        {
            var warnings = result.Log
                .Where(e => e.Level == ActionLogLevel.Warning)
                .Select(e => e.Message).ToList();
            var errors = result.Log
                .Where(e => e.Level == ActionLogLevel.Error)
                .Select(e => e.Message).ToList();

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
                CanRollback     = result.CanRollback,
                RollbackToken   = result.RollbackToken,
                TotalLogEntries = result.Log.Count,
                Warnings        = warnings,
                Errors          = errors,
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StorageVM] RecordHistoryAsync failed: {ex}");
        }
    }

    public void Cleanup() => _cts?.Cancel();
}
