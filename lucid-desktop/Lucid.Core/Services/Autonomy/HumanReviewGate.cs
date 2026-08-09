using Lucid.Services.Timeline;
using Lucid.Services.Infrastructure;

namespace Lucid.Services.Autonomy;

/// <summary>
/// Requires explicit user approval before executing any workflow step
/// that modifies files, system state, or performs destructive operations.
///
/// Every call to <see cref="RequestReviewAsync"/> raises <see cref="ReviewRequired"/>
/// on the UI thread and then awaits the user's Approve() or Deny() callback.
///
/// CRITICAL SAFETY RULES:
///   • ReviewRequired MUST be raised on the UI thread.
///   • Approve() and Deny() callbacks MUST be called from the UI thread.
///   • This gate cannot be bypassed — every RequiresApproval step goes through it.
///   • A timeout of 15 minutes prevents zombie locks; timeout = deny.
///
/// Threading:
///   RequestReviewAsync() runs on the calling thread-pool thread.
///   ReviewRequired is dispatched to the UI thread before raising.
/// </summary>
public sealed class HumanReviewGate
{
    private readonly ITimelineAggregationService _timeline;
    private readonly IUiDispatcher             _uiDispatcher;

    public HumanReviewGate(
        ITimelineAggregationService timeline,
        IUiDispatcher             uiDispatcher)
    {
        _timeline     = timeline;
        _uiDispatcher = uiDispatcher;
    }

    /// <summary>
    /// Raised on the UI thread when a step requires human review before execution.
    /// Subscribe in the companion overlay to show the review card.
    /// </summary>
    public event EventHandler<WorkflowReviewEventArgs>? ReviewRequired;

    /// <summary>
    /// Raises a review card for the given step, waits for the user to approve or deny,
    /// and returns true if approved.
    ///
    /// A 15-minute timeout treats no-response as a denial.
    /// </summary>
    public async Task<bool> RequestReviewAsync(
        AutonomousWorkflow      workflow,
        WorkflowStep            step,
        FileOrganizationPreview? preview,
        string                  reviewNarration,
        CancellationToken       ct = default)
    {
        using var gate    = new SemaphoreSlim(0, 1);
        bool      approved = false;

        var args = new WorkflowReviewEventArgs
        {
            Workflow        = workflow,
            Step            = step,
            Preview         = preview,
            ReviewNarration = reviewNarration,
            Approve         = () =>
            {
                approved = true;
                try { gate.Release(); } catch { }
            },
            Deny            = () =>
            {
                approved = false;
                try { gate.Release(); } catch { }
            },
        };

        // Publish to timeline
        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = TimelineEventType.ConsentRequested,
            OccurredAt = DateTimeOffset.Now,
            Title      = $"Review required: {step.Title}",
            Detail     = reviewNarration,
            Severity   = step.Risk >= Trust.TrustRiskLevel.High
                             ? TimelineEventSeverity.Warning
                             : TimelineEventSeverity.Info,
        });

        // Raise review event on UI thread
        DispatchToUI(() => ReviewRequired?.Invoke(this, args));

        // Wait for response (15 minutes max)
        bool responded = await gate.WaitAsync(TimeSpan.FromMinutes(15), ct)
                                   .ConfigureAwait(false);

        if (!responded || ct.IsCancellationRequested)
            approved = false;  // timeout = deny

        // Record outcome to timeline
        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = approved ? TimelineEventType.ConsentGranted : TimelineEventType.ConsentDenied,
            OccurredAt = DateTimeOffset.Now,
            Title      = approved ? $"Approved: {step.Title}" : $"Declined: {step.Title}",
            Severity   = approved ? TimelineEventSeverity.Good : TimelineEventSeverity.Info,
        });

        return approved;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void DispatchToUI(Action action)
    {
        if (_uiDispatcher.HasThreadAccess)
            action();
        else
            _uiDispatcher.TryEnqueue(() => action());
    }
}
