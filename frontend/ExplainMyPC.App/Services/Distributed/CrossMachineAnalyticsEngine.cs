using System.Text;
using ExplainMyPC.Services.Behavior;

namespace ExplainMyPC.Services.Distributed;

/// <summary>
/// Compares operational snapshots across trusted linked devices to surface
/// cross-machine patterns, divergences, and correlated events.
///
/// All analysis is deterministic and local-only — no ML, no cloud inference.
/// Insights use confidence-aware, probabilistic language per platform doctrine.
/// Insights are regenerated on demand from the current snapshot set.
/// </summary>
public sealed class CrossMachineAnalyticsEngine
{
    // Detection thresholds
    private const double ThermalDivergenceThresholdC = 15.0;
    private const double HighCpuThreshold            = 70.0;
    private const double HighThermalThreshold        = 80.0;
    private const int    HighInsightCountThreshold   =  3;

    private readonly DeviceIdentityService _identity;
    private readonly LocalSyncCoordinator  _sync;

    public CrossMachineAnalyticsEngine(
        DeviceIdentityService identity,
        LocalSyncCoordinator  sync)
    {
        _identity = identity;
        _sync     = sync;
    }

    // ── Insight generation ────────────────────────────────────────────────────

    /// <summary>
    /// Generates cross-machine insights from the current remote snapshot set.
    /// Returns an empty list when no remote devices are online.
    /// </summary>
    public IReadOnlyList<CrossMachineInsight> AnalyzeCurrentState()
    {
        var insights  = new List<CrossMachineInsight>();
        var snapshots = _sync.GetRemoteSnapshots();
        if (snapshots.Count == 0) return insights;

        var myId  = _identity.GetOrCreateIdentity().DeviceId;
        var snaps = snapshots.Values.ToList();

        // ── 1. SimultaneousLoad — shared high CPU across multiple devices ──────
        var highCpuSnaps = snaps.Where(s => s.CpuPercent >= HighCpuThreshold).ToList();
        if (highCpuSnaps.Count > 0)
        {
            var affected = new List<string> { myId };
            affected.AddRange(highCpuSnaps.Select(s => s.SourceDeviceId));
            insights.Add(new CrossMachineInsight(
                InsightId:         $"cross.cpu.simultaneous.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                Title:             "Simultaneous elevated CPU load across devices",
                Explanation:       $"Multiple linked devices are showing elevated CPU usage around the same time. " +
                                   $"Observed on: {string.Join(", ", highCpuSnaps.Select(s => s.DeviceName))}. " +
                                   "This may be worth reviewing — shared triggers like Windows Update, " +
                                   "antivirus scans, or scheduled tasks can explain correlated load.",
                AffectedDeviceIds: affected,
                Type:              CrossInsightType.SimultaneousLoad,
                DetectedAt:        DateTimeOffset.UtcNow,
                Confidence:        highCpuSnaps.Count >= 2
                                       ? BehaviorConfidence.High
                                       : BehaviorConfidence.Medium));
        }

        // ── 2. ThermalDivergence — significant temperature difference ──────────
        var thermalSnaps = snaps.Where(s => s.TemperatureAvailable).ToList();
        for (int i = 0; i < thermalSnaps.Count; i++)
        {
            for (int j = i + 1; j < thermalSnaps.Count; j++)
            {
                var a    = thermalSnaps[i];
                var b    = thermalSnaps[j];
                var diff = Math.Abs(a.CpuTemperatureCelsius - b.CpuTemperatureCelsius);

                if (diff < ThermalDivergenceThresholdC) continue;

                var hotter = a.CpuTemperatureCelsius > b.CpuTemperatureCelsius ? a : b;
                var cooler = hotter == a ? b : a;

                insights.Add(new CrossMachineInsight(
                    InsightId:         $"cross.thermal.diverge.{a.SourceDeviceId[..8]}.{b.SourceDeviceId[..8]}",
                    Title:             "Thermal behavior divergence between devices",
                    Explanation:       $"{hotter.DeviceName} is running approximately {diff:F0}°C warmer than " +
                                       $"{cooler.DeviceName} at similar uptime. This may reflect cooling " +
                                       "differences, ambient temperature variation, workload intensity, or " +
                                       "dust accumulation on the warmer system.",
                    AffectedDeviceIds: [a.SourceDeviceId, b.SourceDeviceId],
                    Type:              CrossInsightType.ThermalDivergence,
                    DetectedAt:        DateTimeOffset.UtcNow,
                    Confidence:        diff >= 25 ? BehaviorConfidence.High : BehaviorConfidence.Medium));
            }
        }

        // ── 3. CommonWorkload — same workload running across multiple devices ───
        var workloadGroups = snaps
            .Where(s => s.CurrentWorkload is not WorkloadType.Unknown and not WorkloadType.Idle)
            .GroupBy(s => s.CurrentWorkload)
            .Where(g => g.Count() >= 2);

        foreach (var group in workloadGroups)
        {
            insights.Add(new CrossMachineInsight(
                InsightId:         $"cross.workload.common.{group.Key}",
                Title:             $"Shared {WorkloadLabel(group.Key)} workload across devices",
                Explanation:       $"Multiple linked devices appear to be running {WorkloadLabel(group.Key)} " +
                                   $"workloads simultaneously: {string.Join(", ", group.Select(s => s.DeviceName))}. " +
                                   "This is expected when devices are used together for a shared task.",
                AffectedDeviceIds: group.Select(s => s.SourceDeviceId).ToList(),
                Type:              CrossInsightType.CommonWorkload,
                DetectedAt:        DateTimeOffset.UtcNow,
                Confidence:        BehaviorConfidence.Medium));
        }

        // ── 4. DeviceSpecificAnomaly — elevated insight count on one device ────
        var highInsightSnaps = snaps
            .Where(s => s.ActiveInsightCount >= HighInsightCountThreshold)
            .ToList();

        // Only flag as device-specific if other devices are stable
        var stableSnaps = snaps
            .Where(s => s.ActiveInsightCount < HighInsightCountThreshold)
            .ToList();

        if (highInsightSnaps.Count > 0 && stableSnaps.Count > 0)
        {
            foreach (var snap in highInsightSnaps)
            {
                insights.Add(new CrossMachineInsight(
                    InsightId:         $"cross.anomaly.isolated.{snap.SourceDeviceId[..8]}",
                    Title:             $"Isolated anomaly pattern on {snap.DeviceName}",
                    Explanation:       $"{snap.DeviceName} has {snap.ActiveInsightCount} active operational " +
                                       "findings while other linked devices appear stable. This suggests a " +
                                       "device-specific issue rather than an environment-wide condition.",
                    AffectedDeviceIds: [snap.SourceDeviceId],
                    Type:              CrossInsightType.DeviceSpecificAnomaly,
                    DetectedAt:        DateTimeOffset.UtcNow,
                    Confidence:        BehaviorConfidence.Medium));
            }
        }

        // ── 5. SharedDegradation — all devices elevated ───────────────────────
        var allElevated = snaps.All(s => s.ActiveInsightCount >= 1) && snaps.Count >= 2;
        if (allElevated)
        {
            insights.Add(new CrossMachineInsight(
                InsightId:         $"cross.degradation.shared.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                Title:             "Operational anomalies present on all linked devices",
                Explanation:       "All linked devices are reporting active operational findings simultaneously. " +
                                   "This pattern sometimes follows a Windows update, network configuration change, " +
                                   "or environmental shift affecting the whole system.",
                AffectedDeviceIds: snaps.Select(s => s.SourceDeviceId).ToList(),
                Type:              CrossInsightType.SharedDegradation,
                DetectedAt:        DateTimeOffset.UtcNow,
                Confidence:        BehaviorConfidence.Low));
        }

        return insights;
    }

    // ── Ecosystem narrative ───────────────────────────────────────────────────

    /// <summary>
    /// Generates a plain-English summary of the current ecosystem state.
    /// Suitable for the Shared Operational Narratives section.
    /// </summary>
    public string GenerateEcosystemNarrative()
    {
        var snapshots = _sync.GetRemoteSnapshots();

        if (snapshots.Count == 0)
            return "No linked devices are currently online. " +
                   "Link another device running ExplainMyPC to enable cross-machine operational awareness.";

        var snaps = snapshots.Values.ToList();
        var sb    = new StringBuilder();

        // Online count
        sb.Append($"{snaps.Count} linked device{(snaps.Count == 1 ? " is" : "s are")} currently synchronized. ");

        // Dominant workload
        var dominantWorkload = snaps
            .GroupBy(s => s.CurrentWorkload)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        if (dominantWorkload is not null &&
            dominantWorkload.Key is not WorkloadType.Unknown and not WorkloadType.Idle)
        {
            sb.Append($"Most devices are running {WorkloadLabel(dominantWorkload.Key)} workloads. ");
        }

        // Elevated thermal conditions
        var hotDevices = snaps
            .Where(s => s.TemperatureAvailable && s.CpuTemperatureCelsius >= HighThermalThreshold)
            .ToList();

        if (hotDevices.Count > 0)
        {
            sb.Append($"{string.Join(", ", hotDevices.Select(s => s.DeviceName))} " +
                      $"{(hotDevices.Count == 1 ? "is" : "are")} running warm. ");
        }

        // Cross-machine insight summary
        var insights = AnalyzeCurrentState();
        if (insights.Count == 0)
            sb.Append("No cross-machine anomalies detected at this time.");
        else
            sb.Append($"{insights.Count} cross-machine pattern{(insights.Count == 1 ? "" : "s")} detected — " +
                      "see the insights section for details.");

        return sb.ToString();
    }

    // ── Device comparison ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a side-by-side operational comparison between this device and a remote device.
    /// Null if the remote device has no current snapshot.
    /// </summary>
    public DeviceComparisonResult? CompareWithDevice(string remoteDeviceId)
    {
        var snapshots = _sync.GetRemoteSnapshots();
        if (!snapshots.TryGetValue(remoteDeviceId, out var remote)) return null;

        var my = _identity.GetOrCreateIdentity();

        return new DeviceComparisonResult(
            DeviceIdA:        my.DeviceId,
            DeviceNameA:      my.DeviceName,
            DeviceIdB:        remote.SourceDeviceId,
            DeviceNameB:      remote.DeviceName,
            AvgCpuA:          0,   // populated externally if needed
            AvgCpuB:          remote.CpuPercent,
            AvgTemperatureA:  0,
            AvgTemperatureB:  remote.CpuTemperatureCelsius,
            StorageUsedPctA:  0,
            StorageUsedPctB:  remote.StorageUsedPercent,
            DominantWorkloadA: WorkloadType.Unknown,
            DominantWorkloadB: remote.CurrentWorkload,
            ComputedAt:        DateTimeOffset.UtcNow);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static string WorkloadLabel(WorkloadType wl) => wl switch
    {
        WorkloadType.Gaming               => "Gaming",
        WorkloadType.Development          => "Development",
        WorkloadType.MediaEditing         => "Media Editing",
        WorkloadType.Rendering            => "Rendering",
        WorkloadType.Streaming            => "Streaming",
        WorkloadType.BrowserResearch      => "Browser Research",
        WorkloadType.Productivity         => "Productivity",
        WorkloadType.BackgroundProcessing => "Background Processing",
        WorkloadType.ThermalIntensive     => "Thermal-Intensive",
        WorkloadType.OvernightProcessing  => "Overnight Processing",
        WorkloadType.StartupHeavy         => "Startup-Heavy",
        WorkloadType.Idle                 => "Idle",
        _                                 => "Unknown",
    };
}
