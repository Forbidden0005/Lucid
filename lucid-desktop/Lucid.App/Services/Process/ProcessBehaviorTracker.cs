using System.Diagnostics;
using Lucid.Helpers;
using Lucid.Services.Security;

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
///   ThreadExplosion   — current thread count > 200
///   HandleLeak        — current handle count > 2000
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
    private const int    ThreadExplosionLimit  = 200;
    private const int    HandleLeakLimit       = 2_000;
    private const float  ZombieCpuMinimum      = 2f;
    private const int    MaxTrackedProcesses   = 300;

    // Per-PID ring buffers
    private sealed class PidHistory
    {
        public readonly Queue<float> Cpu = new(HistoryDepth + 1);
        public readonly Queue<long>  Ram = new(HistoryDepth + 1);
        public DateTime LastSeenAt = DateTime.UtcNow;

        public void PushCpu(float v) { Cpu.Enqueue(v); if (Cpu.Count > HistoryDepth) Cpu.Dequeue(); }
        public void PushRam(long  v) { Ram.Enqueue(v); if (Ram.Count > HistoryDepth) Ram.Dequeue(); }
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
        DateTime startTime, bool hasWindow,
        int parentProcessId, string parentProcessName,
        bool isSigned, string publisher)
    {
        if (!_history.TryGetValue(sample.ProcessId, out var hist))
        {
            hist = new PidHistory();
            if (_history.Count < MaxTrackedProcesses)
                _history[sample.ProcessId] = hist;
        }

        hist.PushCpu(sample.CpuPercent);
        hist.PushRam(sample.RamBytes);
        hist.LastSeenAt = DateTime.UtcNow;

        var anomalies = DetectAnomalies(sample, hist, threadCount, handleCount, hasWindow,
            execPath, commandLine, parentProcessName, isSigned);
        var category   = ProcessClassifier.Classify(sample.ProcessName);
        var critical    = ProcessClassifier.IsCritical(sample.ProcessName);
        var trustLevel  = SignatureVerificationService.ClassifyPublisher(publisher, execPath);

        return new ProcessRecord
        {
            ProcessId         = sample.ProcessId,
            ProcessName       = sample.ProcessName,
            DisplayName       = sample.DisplayName,
            CpuPercent        = sample.CpuPercent,
            RamBytes          = sample.RamBytes,
            ThreadCount       = threadCount,
            HandleCount       = handleCount,
            ExecutablePath    = execPath,
            CompanyName       = companyName,
            CommandLine       = commandLine,
            StartTime         = startTime,
            HasWindow         = hasWindow,
            Category          = category,
            Anomalies         = anomalies,
            IsCritical        = critical,
            ParentProcessId   = parentProcessId,
            ParentProcessName = parentProcessName,
            IsSigned          = isSigned,
            Publisher         = publisher,
            TrustLevel        = trustLevel,
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
        int threadCount, int handleCount, bool hasWindow,
        string execPath, string commandLine, string parentProcessName, bool isSigned)
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

        // Thread explosion
        if (threadCount > ThreadExplosionLimit)
            flags |= ProcessAnomalyFlags.ThreadExplosion;

        // Handle leak
        if (handleCount > HandleLeakLimit)
            flags |= ProcessAnomalyFlags.HandleLeak;

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

        // Masquerading: known system process name running outside its normal directory
        if (ProcessSecurityHeuristics.IsMasquerading(sample.ProcessName, execPath))
            flags |= ProcessAnomalyFlags.MasqueradeSuspected;

        // Suspicious parent/child: office/browser process spawned a scripting or system utility
        if (ProcessSecurityHeuristics.IsSuspiciousParentChild(parentProcessName, sample.ProcessName))
            flags |= ProcessAnomalyFlags.SuspiciousParentChild;

        // LOLBin with obfuscated/hidden command-line arguments
        if (ProcessSecurityHeuristics.IsLolbin(sample.ProcessName) &&
            ProcessSecurityHeuristics.HasSuspiciousCommandLine(commandLine))
            flags |= ProcessAnomalyFlags.LolbinSuspiciousArgs;

        // Unsigned executable running from a Windows system directory
        if (ProcessSecurityHeuristics.IsUnsignedInSystemPath(execPath, isSigned))
            flags |= ProcessAnomalyFlags.UnsignedSystemPath;

        return flags;
    }
}
