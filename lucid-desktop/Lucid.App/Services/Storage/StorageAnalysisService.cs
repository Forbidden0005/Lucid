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
/// </summary>
public sealed class StorageAnalysisService : IStorageAnalysisService
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly TimelineAggregationService?              _timeline;

    private CancellationTokenSource? _cts;

    public StorageAnalysisResult? LastResult { get; private set; }
    public bool                   IsScanning { get; private set; }

    public event EventHandler<StorageAnalysisResult>? ScanCompleted;
    public event EventHandler<StorageScanProgress>?   ScanProgressChanged;

    // Minimum file size to appear in the "large files" list
    private const long LargeFileSizeThreshold  = 50  * 1_048_576L;  // 50 MB
    // Minimum file size to participate in duplicate detection
    private const long DuplicateSizeThreshold  = 100 * 1_024L;      // 100 KB
    private const int  MaxLargeFileResults     = 200;

    public StorageAnalysisService(
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
        TimelineAggregationService?              timeline = null)
    {
        _dispatcher = dispatcher;
        _timeline   = timeline;
    }

    // ── IStorageAnalysisService ───────────────────────────────────────────────

    public async Task StartScanAsync(CancellationToken ct = default)
    {
        if (IsScanning) return;

        _cts      = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsScanning = true;

        AddTimeline("Storage scan started",
            "Lucid is scanning for large files, duplicates, and category breakdown.",
            TimelineEventSeverity.Info, started: true);

        var scanRoot = Path.GetPathRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? @"C:\";

        StorageAnalysisResult result;
        try
        {
            result = await Task.Run(() => RunScan(scanRoot, _cts.Token), _cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new StorageAnalysisResult(
                scanRoot, DateTimeOffset.Now, TimeSpan.Zero,
                0, 0, [], [], [], [], WasCancelled: true);
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }

        if (!result.WasCancelled)
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
