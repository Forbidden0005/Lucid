using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Reasoning.Memory;

/// <summary>
/// Detailed historical record of a single <see cref="OperationalInference"/>,
/// enriched with outcome tracking and pattern metadata.
///
/// Used by <see cref="PatternConsistencyAnalyzer"/> to detect recurring patterns
/// and by <see cref="RecommendationOutcomeTracker"/> to correlate recommendations
/// with their eventual outcomes.
///
/// Immutable — create via <see cref="FromInference"/>.
/// </summary>
public sealed record HistoricalInferenceRecord
{
    /// <summary>Original inference ID for correlation.</summary>
    public required Guid   InferenceId       { get; init; }

    /// <summary>The rule or pattern type that produced this inference (e.g. "StartupCongestion").</summary>
    public required string InferenceType     { get; init; }

    /// <summary>Headline text at time of production.</summary>
    public required string Headline          { get; init; }

    /// <summary>Confidence level at time of production.</summary>
    public required ConfidenceLevel ConfidenceAtTime { get; init; }

    /// <summary>Priority at time of production.</summary>
    public required InferencePriority PriorityAtTime { get; init; }

    /// <summary>Affected domains (e.g. ["Performance", "Startup"]).</summary>
    public required IReadOnlyList<string> Domains { get; init; }

    /// <summary>UTC time this inference was produced.</summary>
    public required DateTime RecordedAtUtc   { get; init; }

    /// <summary>Workload context label at the time of inference.</summary>
    public string? WorkloadContext            { get; init; }

    /// <summary>True if the inference was actionable (had recommended action IDs).</summary>
    public bool WasActionable                { get; init; }

    /// <summary>True if the inference was suppressed by context.</summary>
    public bool WasSuppressed               { get; init; }

    /// <summary>The action IDs that were recommended (if any).</summary>
    public IReadOnlyList<string> RecommendedActionIds { get; init; } = [];

    // ── Factory ────────────────────────────────────────────────────────────────

    /// <summary>Creates a historical record from a live inference.</summary>
    public static HistoricalInferenceRecord FromInference(OperationalInference inference) =>
        new()
        {
            InferenceId          = inference.InferenceId,
            InferenceType        = InferTypeFromHeadline(inference.Headline),
            Headline             = inference.Headline,
            ConfidenceAtTime     = inference.Confidence.Level,
            PriorityAtTime       = inference.Priority,
            Domains              = inference.Domains,
            RecordedAtUtc        = inference.ProducedAt,
            WorkloadContext      = inference.WorkloadContextAtInference,
            WasActionable        = inference.IsActionable,
            WasSuppressed        = inference.IsSuppressed,
            RecommendedActionIds = inference.RecommendedActionIds,
        };

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives a stable inference type string from the headline.
    /// Used for grouping similar inferences across sessions.
    /// </summary>
    private static string InferTypeFromHeadline(string headline)
    {
        if (string.IsNullOrEmpty(headline)) return "Unknown";
        var lower = headline.ToLowerInvariant();
        if (lower.Contains("startup")) return "StartupCongestion";
        if (lower.Contains("thermal")) return "ThermalStress";
        if (lower.Contains("storage") || lower.Contains("disk")) return "StoragePressure";
        if (lower.Contains("multi-domain") || lower.Contains("combined")) return "CombinedPressure";
        if (lower.Contains("session") || lower.Contains("trending")) return "SessionDegradation";
        if (lower.Contains("sustained")) return "SustainedPressure";
        return "General";
    }
}
