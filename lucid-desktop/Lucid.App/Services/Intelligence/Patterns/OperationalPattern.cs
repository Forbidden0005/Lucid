namespace Lucid.Services.Intelligence.Patterns;

/// <summary>
/// Immutable record representing a detected recurring operational pattern.
///
/// An OperationalPattern is formed when the same type of inference or condition
/// recurs multiple times across different sessions or time windows. Patterns are
/// evidence-linked, explainable, and retained only for the configured session window.
///
/// Examples:
///   - "Startup congestion appears every morning (3× this week)"
///   - "Thermal stress correlates with rendering workloads (seen 7 times)"
///   - "RAM pressure occurs after 3+ hours of continuous use (5 occurrences)"
///
/// Privacy guarantee: no user-identifiable data. All fields are operational signals only.
/// </summary>
public sealed record OperationalPattern
{
    /// <summary>Stable identifier derived from the pattern's type + domain fingerprint.</summary>
    public required string PatternKey { get; init; }

    /// <summary>The inference type that recurs (e.g. "StartupCongestion", "ThermalStress").</summary>
    public required string PatternType { get; init; }

    /// <summary>Human-readable description of what this pattern describes.</summary>
    public required string Description { get; init; }

    /// <summary>Number of confirmed occurrences within the observation window.</summary>
    public required int OccurrenceCount { get; init; }

    /// <summary>Operational domains involved (e.g. ["Performance", "Startup"]).</summary>
    public required IReadOnlyList<string> Domains { get; init; }

    /// <summary>
    /// Confidence in the pattern's reliability (0–1).
    /// Grows with occurrence count and stability; shrinks with high variance.
    /// </summary>
    public required double PatternConfidence { get; init; }

    /// <summary>UTC time of the first recorded occurrence.</summary>
    public required DateTime FirstSeenAt { get; init; }

    /// <summary>UTC time of the most recent occurrence.</summary>
    public required DateTime LastSeenAt { get; init; }

    /// <summary>Workload context most frequently associated with this pattern, if any.</summary>
    public string? DominantWorkloadContext { get; init; }

    /// <summary>True when the pattern has consistent occurrence timing (low variance).</summary>
    public bool IsStable { get; init; }

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>True when the pattern is well-established (≥ 5 occurrences, stable).</summary>
    public bool IsWellEstablished => OccurrenceCount >= 5 && IsStable && PatternConfidence >= 0.6;

    /// <summary>Time span from first to most recent occurrence.</summary>
    public TimeSpan PatternAge => LastSeenAt - FirstSeenAt;

    /// <summary>Average time between occurrences (null when fewer than 2 occurrences).</summary>
    public TimeSpan? AverageRecurrenceInterval =>
        OccurrenceCount >= 2 && PatternAge > TimeSpan.Zero
            ? TimeSpan.FromTicks(PatternAge.Ticks / (OccurrenceCount - 1))
            : null;

    /// <summary>Human-readable recurrence summary.</summary>
    public string RecurrenceSummary => OccurrenceCount switch
    {
        1     => "Observed once — establishing pattern.",
        2     => "Observed twice — pattern is emerging.",
        >= 10 => $"Recurring pattern — {OccurrenceCount} confirmed occurrences.",
        _     => $"Observed {OccurrenceCount} times — pattern establishing.",
    };
}
