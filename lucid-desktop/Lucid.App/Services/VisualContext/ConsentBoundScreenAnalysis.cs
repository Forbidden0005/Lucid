using Lucid.Services.Timeline;
using Microsoft.UI.Dispatching;

namespace Lucid.Services.VisualContext;

/// <summary>
/// Gates ALL visual analysis behind an explicit user consent prompt.
///
/// Rules (non-negotiable):
///   • GuideOnly passes get a lightweight prompt; pixel-capture passes get a full card.
///   • NEVER performs any analysis before Approve() is called.
///   • Timeout (90 s) = automatic denial — prevents zombie locks.
///   • No analysis mode runs in the background or fires without an active request.
///
/// Threading:
///   RequestConsentAsync() may be called from any thread.
///   ConsentRequired is always raised on the UI thread.
/// </summary>
public sealed class ConsentBoundScreenAnalysis
{
    private readonly ITimelineAggregationService _timeline;
    private readonly DispatcherQueue             _uiDispatcher;

    // Consent timeout — 90 seconds; after this a non-response = denial.
    private static readonly TimeSpan ConsentTimeout = TimeSpan.FromSeconds(90);

    public ConsentBoundScreenAnalysis(
        ITimelineAggregationService timeline,
        DispatcherQueue             uiDispatcher)
    {
        _timeline     = timeline;
        _uiDispatcher = uiDispatcher;
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread when a visual analysis request needs approval.
    /// Subscribe in CompanionOverlayWindow to surface the consent card.
    /// </summary>
    public event EventHandler<VisualAnalysisConsentEventArgs>? ConsentRequired;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Requests consent for the given analysis mode and reason.
    /// Returns true if the user approves within the timeout; false otherwise.
    ///
    /// GuideOnly mode: lighter prompt; still requires user confirmation.
    /// AnalyzeCurrentWindow: standard consent card.
    /// AnalyzeVisibleDesktop: full-prominence consent card.
    /// </summary>
    public async Task<bool> RequestConsentAsync(
        VisualAnalysisMode mode,
        string             reason,
        string             requestedBy  = "user",
        CancellationToken  ct           = default)
    {
        if (mode == VisualAnalysisMode.Disabled)
            return false;

        using var gate    = new SemaphoreSlim(0, 1);
        bool      approved = false;

        var args = new VisualAnalysisConsentEventArgs
        {
            Mode        = mode,
            Reason      = reason,
            RequestedBy = requestedBy,
            Approve = () =>
            {
                approved = true;
                try { gate.Release(); } catch { /* best-effort: gate may already be released or disposed during shutdown */ }
            },
            Deny = () =>
            {
                approved = false;
                try { gate.Release(); } catch { /* best-effort: gate may already be released or disposed during shutdown */ }
            },
        };

        // Record the consent request to the timeline before raising the event
        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = TimelineEventType.ScreenAnalysisRequested,
            OccurredAt = DateTimeOffset.Now,
            Title      = $"Visual analysis requested: {ModeLabel(mode)}",
            Detail     = reason,
            Severity   = mode == VisualAnalysisMode.AnalyzeVisibleDesktop
                             ? TimelineEventSeverity.Warning
                             : TimelineEventSeverity.Info,
        });

        // Raise consent prompt on the UI thread
        DispatchToUI(() => ConsentRequired?.Invoke(this, args));

        // Block until approved/denied or timeout
        bool responded = await gate.WaitAsync(ConsentTimeout, ct).ConfigureAwait(false);

        if (!responded || ct.IsCancellationRequested)
            approved = false;  // timeout = denial

        // Record outcome
        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = approved ? TimelineEventType.ScreenAnalysisApproved
                                  : TimelineEventType.ConsentDenied,
            OccurredAt = DateTimeOffset.Now,
            Title      = approved
                             ? $"Visual analysis approved: {ModeLabel(mode)}"
                             : $"Visual analysis declined: {ModeLabel(mode)}",
            Severity   = approved ? TimelineEventSeverity.Info : TimelineEventSeverity.Info,
        });

        return approved;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ModeLabel(VisualAnalysisMode mode) => mode switch
    {
        VisualAnalysisMode.GuideOnly              => "Guide (no capture)",
        VisualAnalysisMode.AnalyzeCurrentWindow   => "Active window analysis",
        VisualAnalysisMode.AnalyzeVisibleDesktop  => "Full desktop snapshot",
        _                                         => mode.ToString(),
    };

    private void DispatchToUI(Action action)
    {
        if (_uiDispatcher.HasThreadAccess)
            action();
        else
            _uiDispatcher.TryEnqueue(() => action());
    }
}
