namespace ExplainMyPC.Services.Behavior;

/// <summary>
/// Accumulates completed session summaries and derives a long-term behavioral
/// identity profile for this machine.
///
/// Answers: "What kind of machine is this — primarily gaming, development,
/// media editing, or mixed-use?"
///
/// All data is held in-memory for the application lifetime. The model starts
/// cold and builds confidence as sessions are recorded. This is intentional —
/// keeping it lightweight avoids the need for a dedicated persistence table
/// and ensures the profile reflects recent behavior rather than stale history.
///
/// Confidence levels:
///   Low    — fewer than 5 sessions observed
///   Medium — 5–15 sessions observed
///   High   — 16+ sessions observed
/// </summary>
internal sealed class MachineBehaviorModel
{
    // ── Configuration ─────────────────────────────────────────────────────────

    private const int MaxSessions = 100;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly List<SessionSummary> _sessions = [];

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Records a completed session. Oldest entry evicted when cap is reached.</summary>
    public void RecordSession(SessionSummary summary)
    {
        _sessions.Add(summary);
        if (_sessions.Count > MaxSessions)
            _sessions.RemoveAt(0);
    }

    /// <summary>
    /// Returns the most recent sessions, newest first, capped at <paramref name="count"/>.
    /// </summary>
    public IReadOnlyList<SessionSummary> GetRecentSessions(int count = 20)
    {
        int take = Math.Min(count, _sessions.Count);
        return _sessions.TakeLast(take).Reverse().ToList();
    }

    /// <summary>Returns all sessions for pattern detection purposes.</summary>
    public IReadOnlyList<SessionSummary> GetAllSessions() => _sessions;

    /// <summary>
    /// Derives a <see cref="MachineIdentityProfile"/> from all recorded sessions.
    /// Returns a placeholder profile when no sessions have been recorded.
    /// </summary>
    public MachineIdentityProfile BuildProfile()
    {
        if (_sessions.Count == 0)
        {
            return new MachineIdentityProfile(
                DominantWorkload:      WorkloadType.Unknown,
                SecondaryWorkload:     WorkloadType.Unknown,
                WorkloadBreakdown:     [],
                IdentityNarrative:     "Behavioral profile is building — more sessions will improve the model.",
                ProfiledAt:            DateTimeOffset.Now,
                TotalSessionsAnalyzed: 0,
                Confidence:            BehaviorConfidence.Low);
        }

        // Count sessions per dominant workload
        var counts = new Dictionary<WorkloadType, int>();
        foreach (var s in _sessions)
        {
            counts.TryAdd(s.DominantWorkload, 0);
            counts[s.DominantWorkload]++;
        }

        int total = _sessions.Count;

        // Build sorted breakdown (highest session count first)
        var breakdown = counts
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => (
                Type:     kvp.Key,
                Sessions: kvp.Value,
                SharePct: Math.Round((double)kvp.Value / total * 100.0, 1)))
            .ToList<(WorkloadType Type, int Sessions, double SharePct)>();

        var dominant  = breakdown.Count > 0 ? breakdown[0].Type : WorkloadType.Unknown;
        var secondary = breakdown.Count > 1 ? breakdown[1].Type : WorkloadType.Unknown;

        var confidence = total switch
        {
            < 5  => BehaviorConfidence.Low,
            < 16 => BehaviorConfidence.Medium,
            _    => BehaviorConfidence.High,
        };

        string narrative = BuildNarrative(dominant, secondary, breakdown, total);

        return new MachineIdentityProfile(
            DominantWorkload:      dominant,
            SecondaryWorkload:     secondary,
            WorkloadBreakdown:     breakdown,
            IdentityNarrative:     narrative,
            ProfiledAt:            DateTimeOffset.Now,
            TotalSessionsAnalyzed: total,
            Confidence:            confidence);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static string BuildNarrative(
        WorkloadType dominant,
        WorkloadType secondary,
        IReadOnlyList<(WorkloadType Type, int Sessions, double SharePct)> breakdown,
        int total)
    {
        if (dominant == WorkloadType.Unknown)
            return "Behavioral profile is being established from observed sessions.";

        string domLabel  = Label(dominant);
        string domShare  = breakdown.Count > 0 ? $"{breakdown[0].SharePct:F0}%" : "";
        string intro     = $"This machine is primarily used for {domLabel} ({domShare} of observed sessions).";

        string detail = secondary != WorkloadType.Unknown && secondary != WorkloadType.Unknown
            ? $" {Label(secondary)} sessions are also common."
            : "";

        string context = confidence_phrase(total);

        return intro + detail + " " + context;
    }

    private static string confidence_phrase(int total) => total switch
    {
        < 5  => $"Only {total} session(s) recorded — profile confidence will improve with more data.",
        < 16 => $"Profile based on {total} sessions — medium confidence.",
        _    => $"Profile based on {total} sessions — high confidence.",
    };

    internal static string Label(WorkloadType t) => t switch
    {
        WorkloadType.Gaming              => "Gaming",
        WorkloadType.Development         => "Development",
        WorkloadType.MediaEditing        => "Media Editing",
        WorkloadType.Rendering           => "Rendering",
        WorkloadType.Streaming           => "Streaming",
        WorkloadType.BrowserResearch     => "Browser / Research",
        WorkloadType.Productivity        => "Productivity",
        WorkloadType.BackgroundProcessing => "Background Processing",
        WorkloadType.StartupHeavy        => "Startup-Heavy sessions",
        WorkloadType.ThermalIntensive    => "Thermal-Intensive work",
        WorkloadType.OvernightProcessing => "Overnight Processing",
        WorkloadType.Idle                => "Idle / Light usage",
        _                                => "unclassified workloads",
    };
}
