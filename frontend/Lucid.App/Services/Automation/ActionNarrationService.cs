namespace Lucid.Services.Automation;

/// <summary>
/// Generates plain-English narration text for every automation event.
///
/// Narration principles:
///   • Always explain what is happening AND why
///   • Always tell the user how to stop
///   • Never use technical jargon without a plain-English explanation
///   • Never overstate confidence or certainty
///   • Language must remain calm, specific, and interruptible at any time
///
/// All methods are pure functions — no state, no side effects.
/// </summary>
public sealed class ActionNarrationService
{
    // ── Plan narration ────────────────────────────────────────────────────────

    /// <summary>
    /// Full approval card narration shown before the user approves a plan.
    /// Covers goal, steps count, risk summary, and stop instructions.
    /// </summary>
    public string NarratePlanSummary(AutomationExecutionPlan plan)
    {
        var stepWord = plan.Steps.Count == 1 ? "step" : "steps";
        var riskNote = plan.HighestRisk switch
        {
            AutomationRiskLevel.None or AutomationRiskLevel.Low =>
                "Nothing on your system will change.",
            AutomationRiskLevel.Medium =>
                "Some steps make reversible changes — rollback is available.",
            AutomationRiskLevel.High =>
                "Some steps make significant changes. Review each one carefully.",
            _ => string.Empty,
        };

        return $"I'd like to {plan.Goal.ToLowerInvariant()}. " +
               $"This involves {plan.Steps.Count} {stepWord}. {riskNote} " +
               $"You can cancel at any time.";
    }

    /// <summary>Narration shown when a plan is approved and is about to begin.</summary>
    public string NarrateplanApproved(AutomationExecutionPlan plan)
        => $"Starting — {plan.Goal}. I'll walk you through each step.";

    // ── Step narration ────────────────────────────────────────────────────────

    /// <summary>Narration shown immediately before a step executes.</summary>
    public string NarrateStepStart(AutomationStep step)
        => step.Narration;

    /// <summary>Narration shown when a step completes successfully.</summary>
    public string NarrateStepComplete(AutomationStep step)
    {
        var outcome = string.IsNullOrEmpty(step.ExpectedOutcome)
            ? "Done."
            : step.ExpectedOutcome;
        return $"Done — {step.Title.ToLowerInvariant()}. {outcome}";
    }

    /// <summary>Narration shown when a step fails.</summary>
    public string NarrateStepFailed(AutomationStep step, string error)
        => $"Couldn't complete \"{step.Title}\". {error} " +
           $"I'll stop here — nothing else will run until you decide what to do next.";

    // ── Confirmation request narration ────────────────────────────────────────

    /// <summary>
    /// Full confirmation card narration asking the user to approve a step.
    /// Includes what it does, why, and how to undo it.
    /// </summary>
    public string NarrateConfirmationRequest(AutomationStep step)
    {
        var undoNote = step.RollbackAvailable && !string.IsNullOrEmpty(step.HowToUndo)
            ? $" If you want to undo this later: {step.HowToUndo}"
            : step.RollbackAvailable
                ? " This step can be rolled back."
                : string.Empty;

        return $"{step.Narration}\n\n" +
               $"Why: {step.Rationale}{undoNote}";
    }

    // ── Interrupt narration ───────────────────────────────────────────────────

    /// <summary>Narration surfaced as a persistent banner during execution reminding the user how to stop.</summary>
    public string NarrateHowToStop()
        => "You can cancel at any time — tap the stop button and nothing else will run.";

    /// <summary>Narration shown when the user pauses execution.</summary>
    public string NarratePaused()
        => "Paused. Tap Resume to continue, or Cancel to stop entirely.";

    /// <summary>Narration shown when the user cancels execution.</summary>
    public string NarrateCancelled(int completedSteps)
    {
        if (completedSteps == 0)
            return "Cancelled — nothing was changed.";
        var stepWord = completedSteps == 1 ? "step" : "steps";
        return $"Cancelled. {completedSteps} {stepWord} already completed — those remain in effect.";
    }

    /// <summary>Narration shown when rollback completes.</summary>
    public string NarrateRollbackComplete(int rolledBackCount)
    {
        if (rolledBackCount == 0)
            return "Rollback complete. Nothing was changed.";
        var stepWord = rolledBackCount == 1 ? "step" : "steps";
        return $"Rolled back {rolledBackCount} {stepWord}. Your system is back to where it was.";
    }

    /// <summary>Narration shown when the plan completes successfully.</summary>
    public string NarrateComplete(AutomationExecutionPlan plan)
        => $"Done — {plan.Goal.ToLowerInvariant()}. {plan.ExpectedOutcome}";

    // ── ObserveOnly mode narration ────────────────────────────────────────────

    /// <summary>
    /// Returns guidance text instead of executing, when in ObserveOnly mode.
    /// The user is told how to complete the step themselves.
    /// </summary>
    public string NarrateObserveOnly(AutomationStep step)
        => $"Here's how to do this yourself: {step.Rationale} " +
           $"Once done, {step.ExpectedOutcome ?? "you're all set"}.";

    // ── GuidedAssist mode narration ───────────────────────────────────────────

    /// <summary>
    /// Returns guided instruction text for GuidedAssist mode.
    /// Tells the user what to click or do.
    /// </summary>
    public string NarrateGuidedInstruction(AutomationStep step)
        => $"Next step — {step.Title.ToLowerInvariant()}. {step.Narration} " +
           $"Tap Next when you've done this.";
}
