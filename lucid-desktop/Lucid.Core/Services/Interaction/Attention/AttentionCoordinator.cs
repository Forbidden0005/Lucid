using Lucid.Services.Infrastructure.OperationalState;
using Lucid.Services.Intelligence;
using Lucid.Services.Interaction.Recommendations;

namespace Lucid.Services.Interaction.Attention;

/// <summary>
/// Central attention orchestrator for the Lucid platform.
///
/// The AttentionCoordinator answers one question each cycle:
///   "Should Lucid surface this recommendation right now, or wait?"
///
/// It integrates:
///   - <see cref="CognitiveInterruptBudget"/> — prevents interrupt storms
///   - <see cref="RecommendationCooldownManager"/> — prevents immediate re-surfacing
///   - <see cref="NotificationPriorityEngine"/> — ensures highest-priority items go first
///   - <see cref="IAlertFatigueManager"/> — wraps existing fatigue logic
///   - System workload awareness — suppresses lower-priority items under load
///
/// Trust guarantee: every suppression is logged transparently. Users can always
/// see "why was this recommendation held back?" in the Diagnostics panel.
///
/// Thread-safe: constituent services are all thread-safe.
/// </summary>
public sealed class AttentionCoordinator
{
    private readonly IAlertFatigueManager         _fatigue;
    private readonly IOperationalStateCoordinator _operationalState;
    private readonly CognitiveInterruptBudget     _budget;
    private readonly RecommendationCooldownManager _cooldown;

    public AttentionCoordinator(
        IAlertFatigueManager         fatigue,
        IOperationalStateCoordinator operationalState,
        CognitiveInterruptBudget?    budget   = null,
        RecommendationCooldownManager? cooldown = null)
    {
        _fatigue          = fatigue;
        _operationalState = operationalState;
        _budget           = budget   ?? new CognitiveInterruptBudget();
        _cooldown         = cooldown ?? new RecommendationCooldownManager();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates a recommendation and returns whether it should interrupt now.
    ///
    /// Records the recommendation as "shown" if the result is AllowedToSurface.
    /// </summary>
    public AttentionDecision Evaluate(UnifiedRecommendation recommendation)
    {
        var actionKey   = recommendation.SourceCluster?.LeadAction?.Action?.Id
                          ?? recommendation.ClusterId.ToString();
        var isHighLoad  = IsHighWorkload();

        // 1. Fatigue check — is this recommendation severely fatigued?
        if (recommendation.IsFatigued)
            return AttentionDecision.Suppressed(
                "Recommendation deprioritized due to repeated dismissals.");

        // 2. Cooldown check — was this shown very recently?
        if (!_cooldown.IsEligible(actionKey))
        {
            var remaining = _cooldown.RemainingCooldown(actionKey);
            return AttentionDecision.Suppressed(
                $"Cooling down — eligible to resurface in {(int)remaining.TotalMinutes} min.");
        }

        // 3. Workload priority gate.
        if (!NotificationPriorityEngine.ShouldInterrupt(recommendation, isHighLoad))
            return AttentionDecision.Suppressed(
                isHighLoad
                    ? "Deferred — system is under high load; only urgent items surface now."
                    : "Below current priority threshold.");

        // 4. Budget check — are we over the interrupt limit for this window?
        var isUrgent = recommendation.Severity >= Intelligence.Arbitration.RecommendationSeverity.Stability;
        if (!_budget.TryConsume(isUrgent))
            return AttentionDecision.Suppressed(
                "Interrupt budget exhausted — too many notifications in this window. " +
                "This item will surface shortly.");

        // Allowed to surface — record the show.
        _cooldown.RecordShown(actionKey,
            isUrgent ? CooldownUrgency.Urgent : CooldownUrgency.Standard);

        return AttentionDecision.Allowed();
    }

    /// <summary>
    /// Filters a recommendation list to only those allowed to surface right now,
    /// respecting the budget and priority ordering.
    /// </summary>
    public IReadOnlyList<UnifiedRecommendation> FilterForSurface(
        IReadOnlyList<UnifiedRecommendation> recommendations)
    {
        var ranked  = NotificationPriorityEngine.Rank(recommendations);
        var allowed = new List<UnifiedRecommendation>();

        foreach (var rec in ranked)
        {
            var decision = Evaluate(rec);
            if (decision.IsAllowed)
                allowed.Add(rec);
        }

        return allowed.AsReadOnly();
    }

    /// <summary>Remaining interrupt budget in the current window.</summary>
    public int RemainingBudget => _budget.RemainingBudget;

    // ── Private ───────────────────────────────────────────────────────────────

    private bool IsHighWorkload()
    {
        var state = _operationalState.Current;
        return state.CpuPercent > 80 || state.DegradedServiceCount > 0;
    }
}

/// <summary>
/// The result of an attention decision for a single recommendation.
/// </summary>
public sealed record AttentionDecision(bool IsAllowed, string? SuppressReason)
{
    /// <summary>Creates an "allowed to surface" decision.</summary>
    public static AttentionDecision Allowed() => new(true, null);

    /// <summary>Creates a "suppressed" decision with an explanation.</summary>
    public static AttentionDecision Suppressed(string reason) => new(false, reason);
}
