using System.Diagnostics;
using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Timeline;
using Microsoft.UI.Dispatching;

namespace ExplainMyPC.Services.ProcessIntel;

/// <summary>
/// Orchestrates process intelligence: enriches telemetry snapshots with
/// per-process behavioral analysis and emits timeline events for anomalies.
///
/// Subscribes to <see cref="ITelemetryService.ReadingAvailable"/> and publishes
/// <see cref="SnapshotUpdated"/> on the UI thread after each enrichment cycle.
///
/// Design:
///   Enrichment runs on the telemetry thread (ReadingAvailable arrives there
///   from WindowsTelemetryService). Heavy work (reading Process properties) is
///   guarded with try/catch since processes can exit mid-read.
///   Only the top-50 processes by CPU+RAM are enriched to bound the overhead.
/// </summary>
public sealed class ProcessIntelligenceService
{
    private readonly ITelemetryService           _telemetry;
    private readonly DispatcherQueue             _dispatcher;
    private readonly TimelineAggregationService? _timeline;
    private readonly ProcessBehaviorTracker      _tracker = new();

    // Anomaly debounce: don't re-emit a timeline event for the same PID+anomaly
    // within 5 minutes.
    private readonly Dictionary<(int Pid, ProcessAnomalyFlags Flag), DateTime> _emittedAt = new();

    public ProcessIntelligenceSnapshot? LastSnapshot { get; private set; }
    public event EventHandler<ProcessIntelligenceSnapshot>? SnapshotUpdated;

    public ProcessIntelligenceService(
        ITelemetryService           telemetry,
        DispatcherQueue             dispatcher,
        TimelineAggregationService? timeline = null)
    {
        _telemetry  = telemetry;
        _dispatcher = dispatcher;
        _timeline   = timeline;
    }

    public void Start()  => _telemetry.ReadingAvailable += OnReading;
    public void Stop()   => _telemetry.ReadingAvailable -= OnReading;

    // ── Core pipeline ─────────────────────────────────────────────────────────

    private void OnReading(object? sender, TelemetrySnapshot snap)
    {
        if (snap.TopProcesses.Count == 0) return;

        // Enrich top processes with extended data
        var enriched = new List<ProcessRecord>(snap.TopProcesses.Count);
        foreach (var sample in snap.TopProcesses)
        {
            var record = TryEnrich(sample);
            if (record is not null) enriched.Add(record);
        }

        _tracker.EvictStale();

        var anomalous = enriched.Where(r => r.HasAnomalies).ToList();
        var topCpu    = enriched.OrderByDescending(r => r.CpuPercent).Take(10).ToList();
        var topRam    = enriched.OrderByDescending(r => r.RamBytes).Take(10).ToList();

        var snapshot = new ProcessIntelligenceSnapshot(
            enriched, anomalous, topCpu, topRam, DateTimeOffset.Now);

        // Emit timeline events for new anomalies
        foreach (var record in anomalous)
            EmitAnomalyTimeline(record);

        LastSnapshot = snapshot;
        _dispatcher.TryEnqueue(() => SnapshotUpdated?.Invoke(this, snapshot));
    }

    private ProcessRecord? TryEnrich(ProcessSample sample)
    {
        string execPath    = string.Empty;
        string companyName = string.Empty;
        string commandLine = string.Empty;
        int    threads     = 0;
        int    handles     = 0;
        DateTime startTime = DateTime.Now;
        bool   hasWindow   = false;

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(sample.ProcessId);
            threads   = proc.Threads.Count;
            handles   = proc.HandleCount;
            startTime = proc.StartTime;
            hasWindow = proc.MainWindowHandle != IntPtr.Zero;

            try { execPath = proc.MainModule?.FileName ?? string.Empty; } catch { }

            if (!string.IsNullOrEmpty(execPath))
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(execPath);
                    companyName = vi.CompanyName ?? string.Empty;
                }
                catch { }
            }
        }
        catch
        {
            // Process exited — skip it
            return null;
        }

        return _tracker.Enrich(sample, threads, handles,
            execPath, companyName, commandLine, startTime, hasWindow);
    }

    // ── Timeline event emission ───────────────────────────────────────────────

    private void EmitAnomalyTimeline(ProcessRecord record)
    {
        if (_timeline is null) return;

        foreach (var flag in Enum.GetValues<ProcessAnomalyFlags>())
        {
            if (flag == ProcessAnomalyFlags.None) continue;
            if (!record.Anomalies.HasFlag(flag)) continue;

            var key = (record.ProcessId, flag);
            if (_emittedAt.TryGetValue(key, out var last) &&
                (DateTime.UtcNow - last).TotalMinutes < 5) continue;

            _emittedAt[key] = DateTime.UtcNow;

            string title  = $"{record.DisplayName}: {FlagLabel(flag)}";
            string detail = $"PID {record.ProcessId} · {record.CpuFormatted} CPU · {record.RamFormatted}";
            if (record.ThreadCount > 0) detail += $" · {record.ThreadCount} threads";

            var ev = new TimelineEvent
            {
                Id         = TimelineEvent.NewId(),
                Type       = TimelineEventType.ProcessAnomaly,
                OccurredAt = DateTimeOffset.Now,
                Title      = title,
                Detail     = detail,
                Severity   = flag is ProcessAnomalyFlags.RunawayCpu or
                                     ProcessAnomalyFlags.MemoryGrowth or
                                     ProcessAnomalyFlags.HighRamAbsolute
                    ? TimelineEventSeverity.Warning : TimelineEventSeverity.Info,
                ActionId   = record.ProcessId.ToString(),
            };

            _dispatcher.TryEnqueue(() => _timeline.AddStorageEvent(ev));
        }
    }

    private static string FlagLabel(ProcessAnomalyFlags flag) => flag switch
    {
        ProcessAnomalyFlags.RunawayCpu        => "Runaway CPU",
        ProcessAnomalyFlags.MemoryGrowth      => "Memory growing",
        ProcessAnomalyFlags.ThreadExplosion   => "Thread explosion",
        ProcessAnomalyFlags.HandleLeak        => "Handle leak",
        ProcessAnomalyFlags.RepeatedCrashes   => "Repeated crashes",
        ProcessAnomalyFlags.HighRamAbsolute   => "High RAM usage",
        ProcessAnomalyFlags.ZombieBackground  => "Background resource hog",
        ProcessAnomalyFlags.GpuHeavy         => "GPU intensive",
        _                                     => flag.ToString(),
    };
}
