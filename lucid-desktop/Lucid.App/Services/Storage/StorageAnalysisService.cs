using Lucid.Services.Governance;
using Lucid.Services.Timeline;

namespace Lucid.Services.Storage;

/// <summary>
/// Interface for the storage analysis service.
/// </summary>
public interface IStorageAnalysisService
{
    StorageAnalysisResult? LastResult  { get; }
    bool                   IsScanning  { get; }
    event EventHandler<StorageAnalysisResult>? ScanCompleted;
    event EventHandler<StorageScanProgress>?   ScanProgressChanged;
    Task StartScanAsync(CancellationToken ct = default);
    void CancelScan();
}

/// <summary>Progress snapshot emitted during a scan.</summary>
public sealed record StorageScanProgress(
    int    PercentComplete,
    string Phase,
    int    FilesProcessed,
    long   BytesProcessed);

/// <summary>
/// Orchestrates the storage intelligence scan pipeline:
///   1. FileSystemScanner    — low-priority BFS traversal of the system drive
///   2. StorageCategoryAnalyzer — classifies every file into a category bucket
///   3. DuplicateDetectionService — size prefilter then SHA-256 hash grouping
///   4. Timeline events      — emitted for scan started and scan completed
///
/// Threading:
///   StartScanAsync offloads all I/O to the thread pool. Progress and completion
///   events are marshalled back to the UI thread via the injected DispatcherQueue.
///
/// Resource governance (adoption audit 2026-07-25, site #11):
///   The full pipeline holds the <see cref="WorkloadCategory.StorageScan"/> and
///   <see cref="WorkloadCategory.DuplicateHashing"/> slots (both limit-1), so
///   scans serialize against each other and appear on the governance page.
///   Both slots are acquired up front — nothing else uses DuplicateHashing, and
///   splitting the acquisition mid-pipeline would only add failure modes.
///   When the budget refuses admission, the scan waits on the deferred queue
///   (progress shows the reason) instead of running ungoverned; CancelScan
///   still works while waiting.
/// </summary>
public sealed class StorageAnalysisService : IStorageAnalysisService
{
    private const string ScanWorkloadName    = "Storage analysis scan";
    private const string HashingWorkloadName = "Duplicate detection hashing";

    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly TimelineAggregationService?              _timeline;
    private readonly IRuntimeGovernanceService?               _governance;

    private CancellationTokenSource? _cts;
    private bool _holdsScanSlot;
    private bool _holdsHashingSlot;

    public StorageAnalysisResult? LastResult { get; private set; }
    public bool                   IsScanning { get; private set; }

    public event EventHandler<StorageAnalysisResult>? ScanCompleted;
    public event EventHandler<StorageScanProgress>?   ScanProgressChanged;

    // Minimum file size to appear in the "large files" list
    private const long LargeFileSizeThreshold  = 50  * 1_048_576L;  // 50 MB
    // Minimum file size to participate in duplicate detection
    private const long DuplicateSizeThreshold  = 100 * 1_024L;      // 100 KB
    private const int  MaxLargeFileResults     = 200;

    /// <param name="governance">
    /// Optional so pure/unit contexts can run ungoverned; the production call
    /// site (StorageViewModel) always injects the runtime governance service.
    /// </param>
    public StorageAnalysisService(
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
        TimelineAggregationService?              timeline   = null,
        IRuntimeGovernanceService?               governance = null)
    {
        _dispatcher = dispatcher;
        _timeline   = timeline;
        _governance = governance;
    }

    // ── IStorageAnalysisService ───────────────────────────────────────────────

    public async Task StartScanAsync(CancellationToken ct = default)
    {
        if (IsScanning) return;

        _cts      = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsScanning = true;

        try
        {
            if (!await AcquireGovernanceSlotsAsync(_cts.Token).ConfigureAwait(false))
                return; // deferral expired or scan cancelled while waiting

            await RunScanSessionAsync(_cts.Token).ConfigureAwait(false);
        }
        finally
        {
            ReleaseGovernanceSlots();
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Waits for the StorageScan + DuplicateHashing slots. Returns false when
    /// the deferred wait expired; reports a WasCancelled result when the user
    /// cancels while waiting so the UI unwinds normally.
    /// </summary>
    private async Task<bool> AcquireGovernanceSlotsAsync(CancellationToken ct)
    {
        if (_governance is null) return true;

        try
        {
            _holdsScanSlot = await GovernedWorkRunner.WaitForSlotAsync(
                _governance, WorkloadCategory.StorageScan, ScanWorkloadName, ct,
                onDeferred: reason => ReportProgress(0, $"Waiting to start — {reason}", 0, 0))
                .ConfigureAwait(false);

            if (!_holdsScanSlot)
            {
                ReportProgress(0, "Scan not started — the system stayed busy. Try again later.", 0, 0);
                return false;
            }

            _holdsHashingSlot = await GovernedWorkRunner.WaitForSlotAsync(
                _governance, WorkloadCategory.DuplicateHashing, HashingWorkloadName, ct,
                onDeferred: reason => ReportProgress(0, $"Waiting to start — {reason}", 0, 0))
                .ConfigureAwait(false);

            if (!_holdsHashingSlot)
            {
                ReportProgress(0, "Scan not started — the system stayed busy. Try again later.", 0, 0);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            PublishResult(new StorageAnalysisResult(
                ScanRoot(), DateTimeOffset.Now, TimeSpan.Zero,
                0, 0, [], [], [], [], WasCancelled: true), announce: false);
            return false;
        }
    }

    private void ReleaseGovernanceSlots()
    {
        if (_governance is null) return;

        if (_holdsHashingSlot)
        {
            _governance.ReleaseSlot(WorkloadCategory.DuplicateHashing, HashingWorkloadName);
            _holdsHashingSlot = false;
        }
        if (_holdsScanSlot)
        {
            _governance.ReleaseSlot(WorkloadCategory.StorageScan, ScanWorkloadName);
            _holdsScanSlot = false;
        }
    }

    private static string ScanRoot() => Path.GetPathRoot(
        Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? @"C:\";

    private async Task RunScanSessionAsync(CancellationToken ct)
    {
        AddTimeline("Storage scan started",
            "Lucid is scanning for large files, duplicates, and category breakdown.",
            TimelineEventSeverity.Info, started: true);

        var scanRoot = ScanRoot();

        StorageAnalysisResult result;
        try
        {
            result = await Task.Run(() => RunScan(scanRoot, ct), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new StorageAnalysisResult(
                scanRoot, DateTimeOffset.Now, TimeSpan.Zero,
                0, 0, [], [], [], [], WasCancelled: true);
        }

        PublishResult(result, announce: true);
    }

    private void PublishResult(StorageAnalysisResult result, bool announce)
    {
        if (announce && !result.WasCancelled)
        {
            string detail =
                $"Scanned {result.TotalFilesScanned:N0} files " +
                $"({StorageFormatHelper.FormatBytes(result.TotalBytesScanned)}). " +
                $"Found {result.LargeFiles.Count} large files " +
                $"and {result.DuplicateGroups.Count} duplicate groups " +
                $"({result.WasteFormatted} recoverable waste)." +
                (result.NearDuplicates.Count > 0
                    ? $" {result.NearDuplicates.Count} possible near-duplicate pairs flagged for review."
                    : string.Empty);

            AddTimeline("Storage scan complete", detail,
                result.DuplicateGroups.Count > 0 || result.LargeFiles.Count > 10
                    ? TimelineEventSeverity.Warning : TimelineEventSeverity.Good,
                started: false);
        }

        LastResult = result;
        _dispatcher.TryEnqueue(() => ScanCompleted?.Invoke(this, result));
    }

    public void CancelScan() => _cts?.Cancel();

    // ── Scan pipeline ─────────────────────────────────────────────────────────

    private StorageAnalysisResult RunScan(string root, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var largeFiles           = new List<LargeFileRecord>();
        var duplicateCandidates  = new List<LargeFileRecord>();
        var allRecords           = new List<LargeFileRecord>();
        int filesProcessed       = 0;
        long bytesProcessed      = 0;

        // Phase 1: traverse + classify
        ReportProgress(5, "Scanning drive…", 0, 0);

        foreach (var file in FileSystemScanner.Enumerate(root, ct,
            progressCallback: (count, bytes) =>
            {
                filesProcessed = count;
                bytesProcessed = bytes;
                int pct = Math.Min(60, 5 + (int)(count / 10_000.0 * 55));
                ReportProgress(pct, "Scanning drive…", count, bytes);
            }))
        {
            if (ct.IsCancellationRequested) break;

            long size;
            try { size = file.Length; } catch { continue; }

            var category = StorageCategoryAnalyzer.Classify(file);
            var ext      = file.Extension.ToLowerInvariant();

            DateTime lastMod, lastAcc;
            try  { lastMod = file.LastWriteTimeUtc; }  catch { lastMod = DateTime.MinValue; }
            try  { lastAcc = file.LastAccessTimeUtc; } catch { lastAcc = DateTime.MinValue; }

            var record = new LargeFileRecord(
                file.FullName, size, lastMod, lastAcc, ext, category);

            allRecords.Add(record);
            filesProcessed++;
            bytesProcessed += size;

            if (size >= LargeFileSizeThreshold)
                largeFiles.Add(record);

            if (size >= DuplicateSizeThreshold)
                duplicateCandidates.Add(record);
        }

        if (ct.IsCancellationRequested)
            return new StorageAnalysisResult(root, DateTimeOffset.Now, sw.Elapsed,
                bytesProcessed, filesProcessed, [], [], [], [], WasCancelled: true);

        // Phase 2: category aggregation
        ReportProgress(65, "Categorizing files…", filesProcessed, bytesProcessed);
        var categoryBreakdown = StorageCategoryAnalyzer.Aggregate(allRecords);

        // Phase 3: duplicate detection
        ReportProgress(70, "Detecting duplicates…", filesProcessed, bytesProcessed);
        var duplicateGroups = DuplicateDetectionService.Detect(
            duplicateCandidates, ct,
            progressCallback: (hashed, total) =>
            {
                int pct = 70 + (int)(hashed / (double)Math.Max(1, total) * 25);
                ReportProgress(Math.Min(95, pct),
                    $"Hashing duplicates… {hashed}/{total}",
                    filesProcessed, bytesProcessed);
            });

        if (ct.IsCancellationRequested)
            return new StorageAnalysisResult(root, DateTimeOffset.Now, sw.Elapsed,
                bytesProcessed, filesProcessed, [], [], categoryBreakdown, [], WasCancelled: true);

        // Phase 4: near-duplicate heuristics (review-only; excludes exact-hash pairs)
        ReportProgress(96, "Checking for near-duplicates…", filesProcessed, bytesProcessed);
        var exactPaths = duplicateGroups
            .SelectMany(g => g.Files)
            .Select(f => f.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nearDuplicates = NearDuplicateDetectionService.Detect(
            duplicateCandidates, exactPaths, ct);

        var sortedLarge = largeFiles
            .OrderByDescending(f => f.SizeBytes)
            .Take(MaxLargeFileResults)
            .ToList()
            .AsReadOnly();

        ReportProgress(100, "Complete", filesProcessed, bytesProcessed);
        sw.Stop();

        return new StorageAnalysisResult(
            root, DateTimeOffset.Now, sw.Elapsed,
            bytesProcessed, filesProcessed,
            sortedLarge, duplicateGroups, categoryBreakdown, nearDuplicates,
            WasCancelled: false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ReportProgress(int pct, string phase, int files, long bytes)
    {
        var p = new StorageScanProgress(pct, phase, files, bytes);
        _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, p));
    }

    private void AddTimeline(string title, string detail,
        TimelineEventSeverity severity, bool started)
    {
        if (_timeline is null) return;
        var ev = new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = started
                ? TimelineEventType.StorageScanStarted
                : TimelineEventType.StorageScanCompleted,
            OccurredAt = DateTimeOffset.Now,
            Title      = title,
            Detail     = detail,
            Severity   = severity,
        };
        _dispatcher.TryEnqueue(() => _timeline.AddStorageEvent(ev));
    }
}
