namespace ExplainMyPC.Services.Learning;

/// <summary>
/// Service that learns which remediation actions actually improve system conditions
/// over time, using deterministic local-only operational evidence.
///
/// Design principles:
///   • Purely deterministic — no ML, no probabilistic models, no cloud
///   • Cold-start safe — returns sensible defaults when data is sparse
///   • Confidence-aware — distinguishes warm (≥2 analyses) from cold (0–1)
///   • Incremental — analyzes only newly-seen action executions each pass
///   • Bounded — caps analysis per pass to avoid resource contention
///
/// Data source:
///   Operation history (OperationHistoryService) provides the list of
///   actions to analyze.  OperationalReplayService provides the before/after
///   telemetry comparison.  Results are persisted to:
///   %LOCALAPPDATA%\ExplainMyPC\Learning\outcome-records.json
///
/// Threading:
///   All public members are safe to call from the UI thread.
///   AnalyzePendingActionsAsync() offloads I/O to the thread pool internally.
/// </summary>
public interface IRemediationLearningService
{
    // ── Profile access ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effectiveness profile for the given action key, or null
    /// if no outcome records exist for it yet.
    /// </summary>
    RecommendationEffectivenessProfile? GetProfile(string actionKey);

    /// <summary>
    /// Returns a snapshot of all known effectiveness profiles, keyed by action key.
    /// Returns an empty dictionary before the first analysis pass completes.
    /// </summary>
    IReadOnlyDictionary<string, RecommendationEffectivenessProfile> GetAllProfiles();

    // ── Analysis ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyzes any operation history records that have not yet been evaluated.
    ///
    /// For each pending record:
    ///   • Skips actions that are too recent (&lt; 5 minutes old — no post-action data yet)
    ///   • Builds a before/after comparison via OperationalReplayService
    ///   • Classifies the outcome using StabilizationClassifier
    ///   • Persists the outcome record to the local JSON file
    ///
    /// Capped at <c>MaxAnalysisPerPass</c> records per call to bound resource use.
    /// Raises <see cref="ProfilesUpdated"/> after new records are processed.
    ///
    /// Safe to call multiple times — already-analyzed operations are skipped.
    /// </summary>
    Task AnalyzePendingActionsAsync();

    // ── Change notification ───────────────────────────────────────────────────

    /// <summary>
    /// Raised on the calling thread after <see cref="AnalyzePendingActionsAsync"/>
    /// processes at least one new outcome record and rebuilds the profiles.
    /// </summary>
    event EventHandler? ProfilesUpdated;
}
