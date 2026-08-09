namespace Lucid.Services.Interaction.Recommendations;

/// <summary>
/// Describes the lifecycle state of a recommendation in the Interaction layer.
///
/// A recommendation progresses through states as it is evaluated, acted upon,
/// or aged out. The lifecycle is tracked per recommendation cluster, not per action,
/// so the user sees coherent cluster-level status rather than per-action noise.
/// </summary>
public enum RecommendationLifecycleState
{
    /// <summary>
    /// The recommendation is visible and the user has not yet acted on it.
    /// This is the initial state for all surfaced recommendations.
    /// </summary>
    Active = 0,

    /// <summary>
    /// The recommendation has been filtered by the context suppression engine
    /// because the system context makes it inappropriate to surface right now.
    /// Suppressed recommendations are visible in the "Why was this hidden?" panel.
    /// </summary>
    Suppressed = 1,

    /// <summary>
    /// The underlying condition that triggered this recommendation is no longer
    /// present — the problem resolved on its own or via user action.
    /// </summary>
    Resolved = 2,

    /// <summary>
    /// The user explicitly deferred this recommendation ("remind me later").
    /// It will re-surface after the deferral window expires.
    /// </summary>
    Deferred = 3,

    /// <summary>
    /// The recommendation was surfaced multiple times without action and has
    /// been deprioritized by the fatigue engine. The user can still see it by
    /// scrolling to suppressed items.
    /// </summary>
    Expired = 4,
}

/// <summary>Extension helpers for RecommendationLifecycleState.</summary>
public static class RecommendationLifecycleStateExtensions
{
    /// <summary>Returns a UI-friendly label for the lifecycle state.</summary>
    public static string ToLabel(this RecommendationLifecycleState state) => state switch
    {
        RecommendationLifecycleState.Active    => "Active",
        RecommendationLifecycleState.Suppressed => "Context-filtered",
        RecommendationLifecycleState.Resolved  => "Resolved",
        RecommendationLifecycleState.Deferred  => "Deferred",
        RecommendationLifecycleState.Expired   => "Deprioritized",
        _                                       => "Unknown",
    };

    /// <summary>Returns true for states that should be visible in the main recommendation surface.</summary>
    public static bool IsVisible(this RecommendationLifecycleState state) =>
        state == RecommendationLifecycleState.Active;
}
