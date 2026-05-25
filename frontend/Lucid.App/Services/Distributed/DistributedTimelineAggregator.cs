using Lucid.Services.Behavior;
using Lucid.Services.Timeline;

namespace Lucid.Services.Distributed;

/// <summary>
/// Merges local operational timeline events with remote device snapshots into a
/// unified chronological distributed stream.
///
/// Local events: pulled from ITimelineAggregationService (most recent 30).
/// Remote events: synthesized from SharedOperationalSnapshot objects received by LocalSyncCoordinator.
/// Correlation: events from different devices within a 5-minute window are flagged as correlated.
///
/// All processing is in-memory and local-only — no network I/O.
/// </summary>
public sealed class DistributedTimelineAggregator
{
    private static readonly TimeSpan CorrelationWindow = TimeSpan.FromMinutes(5);

    private readonly DeviceIdentityService       _identity;
    private readonly LocalSyncCoordinator        _sync;
    private readonly ITimelineAggregationService _localTimeline;

    public DistributedTimelineAggregator(
        DeviceIdentityService       identity,
        LocalSyncCoordinator        sync,
        ITimelineAggregationService localTimeline)
    {
        _identity      = identity;
        _sync          = sync;
        _localTimeline = localTimeline;
    }

    /// <summary>
    /// Builds a merged distributed timeline sorted newest-first.
    /// Includes the 30 most recent local events plus one synthetic event per
    /// remote device (derived from its latest snapshot).
    /// Correlated events are flagged with a shared CorrelationGroupId.
    /// </summary>
    public IReadOnlyList<DistributedTimelineEvent> BuildMergedTimeline()
    {
        var events = new List<DistributedTimelineEvent>();
        var myId   = _identity.GetOrCreateIdentity().DeviceId;

        // ── Local events ──────────────────────────────────────────────────────
        foreach (var ev in _localTimeline.Events.Take(30))
        {
            events.Add(new DistributedTimelineEvent(
                EventId:           ev.Id,
                SourceDeviceId:    myId,
                SourceDeviceName:  Environment.MachineName,
                OccurredAt:        ev.OccurredAt,
                Title:             ev.Title,
                Description:       ev.Detail,
                Glyph:             LocalEventGlyph(ev.Type),
                AccentColor:       LocalEventColor(ev.Severity),
                IsLocalDevice:     true,
                IsCorrelated:      false,
                CorrelationGroupId: null));
        }

        // ── Remote snapshot-derived events ────────────────────────────────────
        foreach (var (_, snap) in _sync.GetRemoteSnapshots())
        {
            events.Add(new DistributedTimelineEvent(
                EventId:           $"remote-{snap.SourceDeviceId}-{snap.SnapshotAt.ToUnixTimeSeconds()}",
                SourceDeviceId:    snap.SourceDeviceId,
                SourceDeviceName:  snap.DeviceName,
                OccurredAt:        snap.SnapshotAt,
                Title:             $"{snap.DeviceName} — {WorkloadLabel(snap.CurrentWorkload)}",
                Description:       BuildSnapshotDescription(snap),
                Glyph:             "",
                AccentColor:       WorkloadColor(snap.CurrentWorkload),
                IsLocalDevice:     false,
                IsCorrelated:      false,
                CorrelationGroupId: null));
        }

        // ── Correlation detection ─────────────────────────────────────────────
        MarkCorrelations(events);

        // Sort newest-first
        events.Sort((a, b) => b.OccurredAt.CompareTo(a.OccurredAt));
        return events;
    }

    // ── Correlation detection ─────────────────────────────────────────────────

    private static void MarkCorrelations(List<DistributedTimelineEvent> events)
    {
        // Only attempt correlation when events from multiple devices exist
        var deviceIds = events.Select(e => e.SourceDeviceId).Distinct().ToList();
        if (deviceIds.Count < 2) return;

        var groupCounter = 0;
        for (int i = 0; i < events.Count; i++)
        {
            for (int j = i + 1; j < events.Count; j++)
            {
                var a = events[i];
                var b = events[j];

                // Only correlate events from different devices
                if (a.SourceDeviceId == b.SourceDeviceId) continue;

                // Within the correlation window
                var gap = (a.OccurredAt - b.OccurredAt).Duration();
                if (gap > CorrelationWindow) continue;

                var groupId = $"corr-{groupCounter++}";
                events[i] = events[i] with { IsCorrelated = true, CorrelationGroupId = groupId };
                events[j] = events[j] with { IsCorrelated = true, CorrelationGroupId = groupId };
            }
        }
    }

    // ── Description builder ───────────────────────────────────────────────────

    private static string BuildSnapshotDescription(SharedOperationalSnapshot snap)
    {
        var parts = new List<string>();

        if (snap.CpuPercent > 0)
            parts.Add($"CPU {snap.CpuPercent:F0}%");
        if (snap.RamPercent > 0)
            parts.Add($"RAM {snap.RamPercent:F0}%");
        if (snap.TemperatureAvailable && snap.CpuTemperatureCelsius > 0)
            parts.Add($"{snap.CpuTemperatureCelsius:F0}°C");
        if (snap.StorageUsedPercent > 0)
            parts.Add($"Disk {snap.StorageUsedPercent:F0}%");

        var metrics = parts.Count > 0 ? " · " + string.Join("  ", parts) : string.Empty;
        return $"Uptime {FormatUptime(snap.SystemUptime)}{metrics}";
    }

    private static string FormatUptime(TimeSpan ts)
    {
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m";
        if (ts.TotalHours < 24)   return $"{ts.Hours}h {ts.Minutes}m";
        return $"{(int)ts.TotalDays}d";
    }

    // ── Glyph / color helpers ─────────────────────────────────────────────────

    private static string LocalEventGlyph(TimelineEventType type) => type switch
    {
        TimelineEventType.SystemBoot          => "",
        TimelineEventType.SessionStart        => "",
        TimelineEventType.WakeFromSleep       => "",
        TimelineEventType.IdleBegin           => "",
        TimelineEventType.IdleEnd             => "",
        TimelineEventType.InsightOnset        => "",
        TimelineEventType.InsightResolved     => "",
        TimelineEventType.ForecastAlert       => "",
        TimelineEventType.NarrativeCheckpoint => "",
        TimelineEventType.ActionExecuted      => "",
        TimelineEventType.ActionRollback      => "",
        TimelineEventType.ActionFailed        => "",
        _                                     => "",
    };

    private static string LocalEventColor(TimelineEventSeverity severity) => severity switch
    {
        TimelineEventSeverity.Good     => "#22C55E",
        TimelineEventSeverity.Warning  => "#F59E0B",
        TimelineEventSeverity.Critical => "#EF4444",
        _                              => "#6B7280",
    };

    private static string WorkloadColor(WorkloadType wl) => wl switch
    {
        WorkloadType.Gaming               => "#A855F7",
        WorkloadType.Development          => "#3B82F6",
        WorkloadType.MediaEditing         => "#F59E0B",
        WorkloadType.Rendering            => "#EF4444",
        WorkloadType.Streaming            => "#10B981",
        WorkloadType.BrowserResearch      => "#6366F1",
        WorkloadType.Productivity         => "#22C55E",
        WorkloadType.ThermalIntensive     => "#F97316",
        WorkloadType.BackgroundProcessing => "#8B5CF6",
        _                                 => "#6B7280",
    };

    private static string WorkloadLabel(WorkloadType wl) => wl switch
    {
        WorkloadType.Gaming               => "Gaming",
        WorkloadType.Development          => "Development",
        WorkloadType.MediaEditing         => "Media Editing",
        WorkloadType.Rendering            => "Rendering",
        WorkloadType.Streaming            => "Streaming",
        WorkloadType.BrowserResearch      => "Browser Research",
        WorkloadType.Productivity         => "Productivity",
        WorkloadType.BackgroundProcessing => "Background",
        WorkloadType.StartupHeavy         => "Startup-Heavy",
        WorkloadType.ThermalIntensive     => "Thermal-Intensive",
        WorkloadType.OvernightProcessing  => "Overnight",
        WorkloadType.Idle                 => "Idle",
        _                                 => "Monitoring",
    };
}
