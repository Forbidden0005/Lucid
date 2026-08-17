using System.Diagnostics;
using Lucid.Helpers;

namespace Lucid.Services.ProcessIntel;

/// <summary>
/// Tracks each process over time and detects behavioral anomalies.
///
/// Per-PID history ring buffers (last 20 samples ≈ 30 seconds at 1.5 s poll):
///   CPU%, RAM bytes — for runaway and growth detection.
///
/// Anomaly detection thresholds:
///   RunawayCpu        — CPU% > 80 for 4+ consecutive samples (~6 s)
///   MemoryGrowth      — RAM grew > 200 MB in last 20 samples
///   ThreadGrowth      — thread count climbing steadily (not merely high)
///   HandleGrowth      — handle count climbing steadily (not merely high)
///   HighRamAbsolute   — working set > 1.5 GB
///   ZombieBackground  — CPU > 2%, no visible window, not in known-foreground category
///
/// Threading:
///   Called exclusively from the telemetry background thread. No locks needed.
/// </summary>
internal sealed class ProcessBehaviorTracker
{
    private const int    HistoryDepth          = 20;
    private const float  RunawayCpuThreshold   = 80f;
    private const int    RunawayCpuMinSamples  = 4;
    private const long   MemoryGrowthThreshold = 200L * 1024 * 1024;  // 200 MB
    private const long   HighRamThreshold      = 1_500L * 1024 * 1024; // 1.5 GB
    // Growth-based, deliberately not absolute. A browser or a game holds
    // thousands of handles and hundreds of threads perfectly normally; what
    // distinguishes a leak is that the count keeps climbing. See
    // ResourceGrowthDetector for why all three conditions are needed.
    private const int    GrowthMinSamples      = 12;      // ~18 s at a 1.5 s poll
    private const double HandleGrowthRelative  = 0.20;    // +20% over the window
    private const int    HandleGrowthAbsolute  = 400;     // and at least +400 handles
    private const double ThreadGrowthRelative  = 0.30;    // +30% over the window
    private const int    ThreadGrowthAbsolute  = 24;      // and at least +24 threads
    private const float  ZombieCpuMinimum      = 2f;
    private const int    MaxTrackedProcesses   = 300;

    // Per-PID ring buffers
    private sealed class PidHistory
    {
        public readonly Queue<float> Cpu     = new(HistoryDepth + 1);
        public readonly Queue<long>  Ram     = new(HistoryDepth + 1);
        public readonly Queue<int>   Handles = new(HistoryDepth + 1);
        public readonly Queue<int>   Threads = new(HistoryDepth + 1);
        public DateTime LastSeenAt = DateTime.UtcNow;

        public void PushCpu(float v)     { Cpu.Enqueue(v);     if (Cpu.Count     > HistoryDepth) Cpu.Dequeue(); }
        public void PushRam(long  v)     { Ram.Enqueue(v);     if (Ram.Count     > HistoryDepth) Ram.Dequeue(); }
        public void PushHandles(int v)   { Handles.Enqueue(v); if (Handles.Count > HistoryDepth) Handles.Dequeue(); }
        public void PushThreads(int v)   { Threads.Enqueue(v); if (Threads.Count > HistoryDepth) Threads.Dequeue(); }
    }

    private readonly Dictionary<int, PidHistory> _history = new();
    private          DateTime _lastEviction = DateTime.UtcNow;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Given a raw <see cref="ProcessSample"/> and live <see cref="System.Diagnostics.Process"/>
    /// data, enriches it into a <see cref="ProcessRecord"/> with anomaly detection.
    /// </summary>
    internal ProcessRecord Enrich(ProcessSample sample, int threadCount, int handleCount,
        string execPath, string companyName, string commandLine,
        DateTime startTime, bool hasWindow)
    {
        if (!_history.TryGetValue(sample.ProcessId, out var hist))
        {
            hist = new PidHistory();
            if (_history.Count < MaxTrackedProcesses)
                _history[sample.ProcessId] = hist;
        }

        hist.PushCpu(sample.CpuPercent);
        hist.PushRam(sample.RamBytes);
        hist.PushHandles(handleCount);
        hist.PushThreads(threadCount);
        hist.LastSeenAt = DateTime.UtcNow;

        var anomalies = DetectAnomalies(sample, hist, threadCount, handleCount, hasWindow);
        var category  = ProcessClassifier.Classify(sample.ProcessName);
        var critical  = ProcessClassifier.IsCritical(sample.ProcessName);

        return new ProcessRecord
        {
            ProcessId      = sample.ProcessId,
            ProcessName    = sample.ProcessName,
            DisplayName    = sample.DisplayName,
            CpuPercent     = sample.CpuPercent,
            RamBytes       = sample.RamBytes,
            ThreadCount    = threadCount,
            HandleCount    = handleCount,
            ExecutablePath = execPath,
            CompanyName    = companyName,
            CommandLine    = commandLine,
            StartTime      = startTime,
            HasWindow      = hasWindow,
            Category       = category,
            Anomalies      = anomalies,
            IsCritical     = critical,
        };
    }

    /// <summary>Removes history for PIDs not seen in the last 120 seconds.</summary>
    internal void EvictStale()
    {
        if ((DateTime.UtcNow - _lastEviction).TotalSeconds < 60) return;
        _lastEviction = DateTime.UtcNow;
        var cutoff = DateTime.UtcNow.AddSeconds(-120);
        foreach (var pid in _history.Keys
            .Where(k => _history[k].LastSeenAt < cutoff).ToList())
            _history.Remove(pid);
    }

    // ── Anomaly detection ─────────────────────────────────────────────────────

    private static ProcessAnomalyFlags DetectAnomalies(
        ProcessSample sample, PidHistory hist,
        int threadCount, int handleCount, bool hasWindow)
    {
        var flags = ProcessAnomalyFlags.None;

        // Runaway CPU: last N samples all above threshold
        if (hist.Cpu.Count >= RunawayCpuMinSamples &&
            hist.Cpu.TakeLast(RunawayCpuMinSamples).All(c => c >= RunawayCpuThreshold))
            flags |= ProcessAnomalyFlags.RunawayCpu;

        // Memory growth: newest RAM much higher than oldest in history
        if (hist.Ram.Count >= 8)
        {
            long oldest = hist.Ram.First();
            long newest = hist.Ram.Last();
            if (newest - oldest > MemoryGrowthThreshold && newest > oldest)
                flags |= ProcessAnomalyFlags.MemoryGrowth;
        }

        // High RAM absolute
        if (sample.RamBytes > HighRamThreshold)
            flags |= ProcessAnomalyFlags.HighRamAbsolute;

        // Thread count climbing. Not "over 200" — Discord, Chrome and most games
        // sit well above that while behaving perfectly normally, and reporting
        // them as broken buries the processes that genuinely are.
        if (ResourceGrowthDetector.IsSustainedGrowth(
                hist.Threads, GrowthMinSamples, ThreadGrowthRelative, ThreadGrowthAbsolute))
            flags |= ProcessAnomalyFlags.ThreadGrowth;

        // Handle count climbing. Same reasoning: the shape matters, not the size.
        if (ResourceGrowthDetector.IsSustainedGrowth(
                hist.Handles, GrowthMinSamples, HandleGrowthRelative, HandleGrowthAbsolute))
            flags |= ProcessAnomalyFlags.HandleGrowth;

        // Zombie background: moderate CPU, no window, not a known service
        if (!hasWindow && sample.CpuPercent >= ZombieCpuMinimum)
        {
            var cat = ProcessClassifier.Classify(sample.ProcessName);
            if (cat is not ProcessCategory.Service and
                       not ProcessCategory.Security and
                       not ProcessCategory.SystemCritical and
                       not ProcessCategory.Runtime)
                flags |= ProcessAnomalyFlags.ZombieBackground;
        }

        return flags;
    }
}
