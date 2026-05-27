using Lucid.Services.Automation;
using Lucid.Services.Timeline;

namespace Lucid.Services.Trust;

/// <summary>
/// Central consent authority for the automation framework.
///
/// All automation actions that could modify system state must pass through
/// <see cref="RequestConsentAsync"/> before executing. This service:
///
///   1. Checks the action against <see cref="AutomationBoundaryPolicy"/> —
///      hard-blocked actions are rejected immediately, before any consent UI is shown.
///
///   2. Evaluates the current <see cref="TrustConsentMode"/> and <see cref="TrustRiskLevel"/>
///      to determine whether a confirmation card is required.
///
///   3. If confirmation is required, raises <see cref="ConsentRequired"/> and waits
///      (with a configurable timeout) for the user to approve or deny.
///
///   4. Forwards the result to the <see cref="AutomationAuditService"/> ledger and
///      the <see cref="OperationalTrustManager"/> for trust-posture adaptation.
///
/// CRITICAL SAFETY RULES:
///   • Consent mode can ONLY be changed by an explicit user gesture (from Settings UI).
///   • The boundary policy check CANNOT be bypassed regardless of consent mode.
///   • Approve() / Deny() callbacks must be called from the UI thread.
///   • This service is NOT re-entrant — only one consent request at a time is supported.
///
/// Threading:
///   <see cref="RequestConsentAsync"/> runs on the calling thread.
///   The <see cref="ConsentRequired"/> event is raised on the UI thread via DispatcherQueue.
///   Approve/Deny callbacks unblock the awaiter from the UI thread.
/// </summary>
public sealed class AutomationConsentService
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ITimelineAggregationService  _timeline;
    private readonly AutomationAuditService       _audit;
    private readonly ConsentExplanationService    _explanations;
    private readonly AutomationTransparencyEngine _transparency;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcher;

    // ── State ─────────────────────────────────────────────────────────────────

    private TrustConsentMode _mode = TrustConsentMode.AskForMediumAndHighRisk;

    // ── Constructor ───────────────────────────────────────────────────────────

    public AutomationConsentService(
        ITimelineAggregationService  timeline,
        AutomationAuditService       audit,
        ConsentExplanationService    explanations,
        AutomationTransparencyEngine transparency,
        Microsoft.UI.Dispatching.DispatcherQueue uiDispatcher)
    {
        _timeline     = timeline;
        _audit        = audit;
        _explanations = explanations;
        _transparency = transparency;
        _uiDispatcher = uiDispatcher;
    }

    // ── Mode management ───────────────────────────────────────────────────────

    /// <summary>Current consent mode. Default: AskForMediumAndHighRisk.</summary>
    public TrustConsentMode CurrentMode => _mode;

    /// <summary>
    /// Sets the consent mode.
    /// MUST only be called in direct response to an explicit user gesture in the Settings UI.
    /// Never call this from automation logic, LLM output, or observed content.
    /// </summary>
    public void SetMode(TrustConsentMode mode)
    {
        _mode = mode;
        ModeChanged?.Invoke(this, mode);
    }

    /// <summary>Raised when the user changes the consent mode.</summary>
    public event EventHandler<TrustConsentMode>? ModeChanged;

    /// <summary>Raised when a consent card needs to be shown in the companion overlay.</summary>
    public event EventHandler<ConsentCardEventArgs>? ConsentRequired;

    // ── Core consent gate ─────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates whether the given step requires consent, requests it if needed,
    /// and returns true if the step is approved to execute.
    ///
    /// Called by <see cref="AutomationOrchestrator"/> before each step's execution gate.
    ///
    /// Returns:
    ///   true  — step is approved (either auto-approved or user-confirmed)
    ///   false — step was denied, blocked, or timed out
    /// </summary>
    public async Task<bool> RequestConsentAsync(
        AutomationStep   step,
        string           workflowId,
        string?          activeInsight = null,
        string?          attributedProcess = null,
        CancellationToken ct = default)
    {
        // ── 1. Boundary policy check (unconditional) ──────────────────────────
        var boundaryCheck = AutomationBoundaryPolicy.CheckActionId(step.ActionId);
        if (boundaryCheck.IsBlocked)
        {
            PublishTimelineEvent(
                Timeline.TimelineEventType.AutomationBoundaryBlocked,
                $"Blocked: {step.Title}",
                $"{boundaryCheck.Reason}",
                TimelineEventSeverity.Warning);

            _audit.RecordBlockedAction(step, workflowId, boundaryCheck.Reason ?? "Boundary policy violation");
            return false;
        }

        // ── 2. Determine scope & risk ─────────────────────────────────────────
        var scope = MapActionIdToScope(step.ActionId, step.Risk);
        var risk  = MapAutomationRiskToTrust(step.Risk);

        // ── 3. Check if consent is required ───────────────────────────────────
        if (!RequiresConsent(risk))
        {
            // Auto-approved — log for audit trail but no UI interaction needed
            _audit.RecordAutoApproved(step, workflowId, scope);
            return true;
        }

        // ── 4. Observe-only / guided: always deny execution ───────────────────
        if (_mode == TrustConsentMode.ObserveOnly || _mode == TrustConsentMode.GuidedOnly)
        {
            _audit.RecordDenied(step, workflowId, scope,
                reason: $"Consent mode is '{_mode}' — action was described but not executed.");
            return false;
        }

        // ── 5. Build consent request ──────────────────────────────────────────
        var definition = PermissionScopeRegistry.Get(scope);
        var whySuggested = _transparency.ExplainWhySuggested(step, activeInsight, attributedProcess);

        var request = new ConsentRequest
        {
            Id                = ConsentRequest.NewId(),
            WorkflowId        = workflowId,
            ActionTitle       = step.Title,
            Scope             = scope,
            Risk              = risk,
            WhySuggested      = whySuggested,
            WhatChanges       = _explanations.ExplainWhatChanges(scope, step.Title),
            HowToUndo         = _explanations.ExplainHowToUndo(scope),
            CanRollback       = definition.Reversible,
            RequiresElevation = definition.RequiresElevation,
            RequestedAt       = DateTimeOffset.Now,
        };

        // ── 6. Show consent card and wait ─────────────────────────────────────
        using var gate = new SemaphoreSlim(0, 1);
        bool approved = false;

        var eventArgs = new ConsentCardEventArgs
        {
            Request  = request,
            Approve  = () =>
            {
                approved = true;
                try { gate.Release(); } catch { /* already released */ }
            },
            Deny     = () =>
            {
                approved = false;
                try { gate.Release(); } catch { /* already released */ }
            },
        };

        PublishTimelineEvent(
            Timeline.TimelineEventType.ConsentRequested,
            $"Consent requested: {step.Title}",
            $"Risk: {_explanations.GetRiskLabel(risk)}. {whySuggested}",
            TimelineEventSeverity.Info);

        DispatchToUI(() => ConsentRequired?.Invoke(this, eventArgs));

        // Wait up to 10 minutes for the user to respond
        bool gotResponse = await gate.WaitAsync(TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);

        if (!gotResponse || ct.IsCancellationRequested)
        {
            // Timed out or cancelled — treat as denial
            approved = false;
        }

        // ── 7. Record response and publish timeline event ─────────────────────
        var response = new ConsentResponse
        {
            RequestId    = request.Id,
            Approved     = approved,
            RespondedAt  = DateTimeOffset.Now,
            DecisionTime = DateTimeOffset.Now - request.RequestedAt,
        };

        if (approved)
        {
            _audit.RecordApproved(step, workflowId, scope, request, response);
            ConsentGranted?.Invoke(this, response);
            PublishTimelineEvent(
                Timeline.TimelineEventType.ConsentGranted,
                $"Approved: {step.Title}",
                null,
                TimelineEventSeverity.Good);
        }
        else
        {
            _audit.RecordDenied(step, workflowId, scope, request, response);
            ConsentDenied?.Invoke(this, response);
            PublishTimelineEvent(
                Timeline.TimelineEventType.ConsentDenied,
                $"Declined: {step.Title}",
                null,
                TimelineEventSeverity.Info);
        }

        return approved;
    }

    /// <summary>Raised when the user approves a consent request.</summary>
    public event EventHandler<ConsentResponse>? ConsentGranted;

    /// <summary>Raised when the user denies a consent request.</summary>
    public event EventHandler<ConsentResponse>? ConsentDenied;

    // ── RequiresConsent logic ─────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given risk level requires a consent card under the current mode.
    /// </summary>
    public bool RequiresConsent(TrustRiskLevel risk) => _mode switch
    {
        TrustConsentMode.ObserveOnly             => false,   // no execution at all
        TrustConsentMode.GuidedOnly              => false,   // no execution at all
        TrustConsentMode.AskAlways               => true,    // always ask
        TrustConsentMode.AskForMediumAndHighRisk => risk >= TrustRiskLevel.Medium,
        TrustConsentMode.AskHighRiskOnly         => risk >= TrustRiskLevel.High,
        _                                        => true,    // unknown mode → always ask
    };

    // ── Scope / risk mapping ──────────────────────────────────────────────────

    private static PermissionScope MapActionIdToScope(string actionId, AutomationRiskLevel automationRisk)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return PermissionScope.SystemToolLaunch;
        var lower = actionId.ToLowerInvariant();

        if (lower.StartsWith("automation.navigate", StringComparison.Ordinal) ||
            lower.StartsWith("automation.explorer", StringComparison.Ordinal))
            return PermissionScope.ExplorerNavigation;

        if (lower.StartsWith("automation.launch", StringComparison.Ordinal))
            return PermissionScope.SystemToolLaunch;

        if (lower.Contains("temp-files"))    return PermissionScope.CacheCleanup;
        if (lower.Contains("browser-cache")) return PermissionScope.CacheCleanup;
        if (lower.Contains("windows-update-cache")) return PermissionScope.CacheCleanup;
        if (lower.Contains("delivery-optimization")) return PermissionScope.CacheCleanup;
        if (lower.Contains("recycle-bin"))   return PermissionScope.RecycleBinEmpty;

        if (lower.Contains("startup"))
            return lower.Contains("disable") || lower.Contains("enable")
                ? PermissionScope.StartupManagement
                : PermissionScope.SystemInfoRead;

        if (lower.Contains("sfc") || lower.Contains("dism")) return PermissionScope.WindowsRepair;
        if (lower.Contains("dns") || lower.Contains("winsock") || lower.Contains("network-adapter"))
            return PermissionScope.NetworkReset;

        if (lower.Contains("windows-store")) return PermissionScope.AppReset;
        if (lower.Contains("terminate"))     return PermissionScope.ProcessTermination;
        if (lower.Contains("large-file"))    return PermissionScope.LargeFileDeletion;
        if (lower.Contains("duplicate"))     return PermissionScope.DuplicateFileDeletion;
        if (lower.Contains("downloads"))     return PermissionScope.OldDownloadsDeletion;

        // Fall back based on risk
        return automationRisk >= AutomationRiskLevel.High
            ? PermissionScope.WindowsRepair
            : PermissionScope.SystemToolLaunch;
    }

    private static TrustRiskLevel MapAutomationRiskToTrust(AutomationRiskLevel risk) => risk switch
    {
        AutomationRiskLevel.None   => TrustRiskLevel.None,
        AutomationRiskLevel.Low    => TrustRiskLevel.Low,
        AutomationRiskLevel.Medium => TrustRiskLevel.Medium,
        AutomationRiskLevel.High   => TrustRiskLevel.High,
        _                          => TrustRiskLevel.High,
    };

    // ── Timeline / dispatch ───────────────────────────────────────────────────

    private void PublishTimelineEvent(
        Timeline.TimelineEventType  type,
        string                      title,
        string?                     detail,
        TimelineEventSeverity       severity)
    {
        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = type,
            OccurredAt = DateTimeOffset.Now,
            Title      = title,
            Detail     = detail,
            Severity   = severity,
        });
    }

    private void DispatchToUI(Action action)
    {
        if (_uiDispatcher.HasThreadAccess)
            action();
        else
            _uiDispatcher.TryEnqueue(() => action());
    }
}

/// <summary>
/// Event args for a consent card that needs to be shown in the companion overlay.
/// </summary>
public sealed class ConsentCardEventArgs : EventArgs
{
    /// <summary>The consent request to display.</summary>
    public required ConsentRequest Request { get; init; }

    /// <summary>Call this to approve the request. Must be called from the UI thread.</summary>
    public Action? Approve { get; init; }

    /// <summary>Call this to deny the request. Must be called from the UI thread.</summary>
    public Action? Deny    { get; init; }
}
