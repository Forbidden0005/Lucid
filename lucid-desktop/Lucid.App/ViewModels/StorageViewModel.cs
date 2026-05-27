using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Execution;
using Lucid.Services.History;
using Lucid.Services.Storage;
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

    public DuplicateGroupViewModel(DuplicateFileGroup g) => Group = g;
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
    private readonly DispatcherQueue _dispatcher;

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

    private static Visibility V(bool show) =>
        show ? Visibility.Visible : Visibility.Collapsed;

    // ── Result collections ────────────────────────────────────────────────────

    public ObservableCollection<LargeFileViewModel>     LargeFiles      { get; } = new();
    public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = new();
    public ObservableCollection<CategoryRowViewModel>    Categories      { get; } = new();
    public ObservableCollection<LargeFileViewModel>      OldDownloads    { get; } = new();

    // ── Scan service ──────────────────────────────────────────────────────────

    private CancellationTokenSource? _cts;

    // ── Status text ───────────────────────────────────────────────────────────

    [ObservableProperty] private string _statusText = string.Empty;

    // ── Construction ──────────────────────────────────────────────────────────

    public StorageViewModel()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()
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
        Categories.Clear();
        OldDownloads.Clear();

        // Build the service with the current timeline if available
        var timeline = AppServices.Timeline as Services.Timeline.TimelineAggregationService;
        var svc      = new StorageAnalysisService(_dispatcher, timeline);

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

            var result = await AppServices.ExecutionEngine
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

            var result = await AppServices.ExecutionEngine
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

        // Populate duplicates
        DuplicateGroups.Clear();
        foreach (var g in result.DuplicateGroups)
            DuplicateGroups.Add(new DuplicateGroupViewModel(g));

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

    private static async Task RecordHistoryAsync(
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

            await AppServices.HistoryService.RecordAsync(new OperationRecord
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
