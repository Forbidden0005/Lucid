namespace ExplainMyPC.Services.Behavior;

/// <summary>
/// Enriches recommendations with behavioral workload context.
///
/// Takes a base recommendation (action ID + title + rationale) and adds:
///   • Workload-appropriate context ("especially relevant during development sessions")
///   • Timing appropriateness flags ("deferred — not recommended during gaming")
///   • Machine identity context ("this machine primarily runs gaming workloads")
///
/// This engine is a pure enrichment layer — it never blocks, defers, or cancels
/// actions unilaterally. It only adds context that the caller can act on.
///
/// Usage:
///   Call Enrich() for any recommendation before displaying it. The returned
///   ContextualRecommendation contains all the information needed to show an
///   appropriately contextualized recommendation card in the UI.
/// </summary>
public sealed class ContextualRecommendationEngine
{
    private readonly IBehavioralContextEngine _context;

    public ContextualRecommendationEngine(IBehavioralContextEngine context)
        => _context = context;

    /// <summary>
    /// Enriches a recommendation with behavioral context.
    /// Never throws — returns the base rationale unmodified on any failure.
    /// </summary>
    public ContextualRecommendation Enrich(
        string actionId,
        string title,
        string baseRationale)
    {
        try
        {
            return EnrichCore(actionId, title, baseRationale);
        }
        catch
        {
            // Enrichment must never break recommendation display
            return new ContextualRecommendation(
                ActionId:            actionId,
                Title:               title,
                ContextualRationale: baseRationale,
                Confidence:          BehaviorConfidence.Low,
                IsTimingAppropriate: true,
                DeferralReason:      null);
        }
    }

    private ContextualRecommendation EnrichCore(
        string actionId,
        string title,
        string baseRationale)
    {
        var ctx             = _context.GetCurrentContext();
        var currentWorkload = ctx.CurrentWorkload.PrimaryWorkload;
        bool deferred       = _context.ShouldDeferRecommendation(actionId, currentWorkload);

        string? deferReason = deferred
            ? $"Deferred — running this during a {WorkloadLabel(currentWorkload)} session " +
              "may interfere with performance."
            : null;

        string enrichedRationale = BuildRationale(
            baseRationale, currentWorkload, ctx.MachineIdentity, deferred);

        return new ContextualRecommendation(
            ActionId:            actionId,
            Title:               title,
            ContextualRationale: enrichedRationale,
            Confidence:          ctx.CurrentWorkload.Confidence,
            IsTimingAppropriate: !deferred,
            DeferralReason:      deferReason);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static string BuildRationale(
        string                 baseRationale,
        WorkloadType           currentWorkload,
        MachineIdentityProfile identity,
        bool                   deferred)
    {
        if (deferred)
        {
            return baseRationale +
                   $" Note: this action is typically better run outside of {WorkloadLabel(currentWorkload)} sessions.";
        }

        // When the dominant workload is known, add machine-identity context
        if (identity.DominantWorkload == WorkloadType.Unknown ||
            identity.TotalSessionsAnalyzed < 3)
        {
            return baseRationale;
        }

        string domLabel = WorkloadLabel(identity.DominantWorkload);
        return baseRationale +
               $" This is especially relevant for {domLabel}-oriented machines like this one.";
    }

    private static string WorkloadLabel(WorkloadType t) => t switch
    {
        WorkloadType.Gaming              => "gaming",
        WorkloadType.Development         => "development",
        WorkloadType.MediaEditing        => "media editing",
        WorkloadType.Rendering           => "rendering",
        WorkloadType.Streaming           => "streaming",
        WorkloadType.BrowserResearch     => "browser/research",
        WorkloadType.Productivity        => "productivity",
        WorkloadType.BackgroundProcessing => "background processing",
        WorkloadType.StartupHeavy        => "startup-heavy",
        WorkloadType.OvernightProcessing => "overnight",
        WorkloadType.Idle                => "idle",
        _                               => "general",
    };
}
