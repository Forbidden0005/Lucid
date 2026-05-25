using Lucid.Services.Learning;

namespace Lucid.Services.Intelligence;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Computes a fatigue suppression factor for recommended actions that have
/// been shown repeatedly without being accepted.
///
/// The factor is a multiplier in [0, 1] applied to the priority score:
///   1.0 → no suppression (first time shown, or user recently accepted it)
///   0.0 → fully suppressed (shown many times, user never acts)
///
/// Fatigue suppression is always transparent — the UI shows when and why
/// a recommendation has been de-prioritized.  The user can always override
/// by explicitly clicking a suppressed recommendation.
///
/// Local-only. Deterministic. No hidden scoring.
/// </summary>
public interface IAlertFatigueManager
{
    /// <summary>
    /// Returns the fatigue suppression factor for the given action key.
    /// Factor is in [0, 1]; 1.0 = not fatigued, lower = increasingly suppressed.
    /// </summary>
    double GetFatigueFactor(string actionKey, AlertTolerance tolerance);

    /// <summary>
    /// Short note describing the suppression level (empty when factor is 1.0).
    /// Example: "Shown 5× in 7 days without action"
    /// </summary>
    string GetFatigueNote(string actionKey, AlertTolerance tolerance);
}

// ── Implementation ─────────────────────────────────────────────────────────────

/// <summary>
/// Computes alert fatigue from InterventionMemoryService recent show-counts.
///
/// Suppression curve per AlertTolerance:
///   Low    — suppression starts after 2 repeated dismissals in 7 days
///   Medium — suppression starts after 4 repeated dismissals in 7 days
///   High   — suppression starts after 7 repeated dismissals in 7 days
///
/// Formula (linear decay):
///   dismissed = shows − acceptances in window
///   suppression = clamp((dismissed − threshold) / (2 × threshold), 0, 0.6)
///   factor = 1.0 − suppression
///
/// Maximum suppression is 60% (factor ≥ 0.4) so even fatigued items
/// remain visible and clickable.
/// </summary>
public sealed class AlertFatigueManager : IAlertFatigueManager
{
    private readonly IInterventionMemoryService _memory;

    public AlertFatigueManager(IInterventionMemoryService memory)
    {
        _memory = memory;
    }

    public double GetFatigueFactor(string actionKey, AlertTolerance tolerance)
    {
        int shows     = _memory.GetRecentShowCount(actionKey, withinDays: 7);
        int accepted  = _memory.GetByCategory("*").Count(r =>
            string.Equals(r.ActionKey, actionKey, StringComparison.OrdinalIgnoreCase) &&
            r.Accepted &&
            r.ShownAt >= DateTimeOffset.Now.AddDays(-7));
        int dismissed = Math.Max(0, shows - accepted);

        int threshold = tolerance switch
        {
            AlertTolerance.Low  => 2,
            AlertTolerance.High => 7,
            _                   => 4, // Medium
        };

        if (dismissed <= threshold) return 1.0;

        double suppression = Math.Clamp(
            (double)(dismissed - threshold) / (2 * threshold), 0.0, 0.6);
        return 1.0 - suppression;
    }

    public string GetFatigueNote(string actionKey, AlertTolerance tolerance)
    {
        double factor = GetFatigueFactor(actionKey, tolerance);
        if (factor >= 1.0) return string.Empty;

        int shows = _memory.GetRecentShowCount(actionKey, withinDays: 7);
        if (shows <= 0) return string.Empty;

        return $"Shown {shows}× this week without action";
    }
}
