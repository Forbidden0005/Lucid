using Lucid.Services.Infrastructure;
using Lucid.Services.Automation;
using Lucid.Services.Timeline;

namespace Lucid.Services.Trust;

/// <summary>
/// Central consent authority for the automation framework.
///
/// All automation actions that could modify system state must pass through
/// <see cref="RequestConsentAsync"/> before executing. This service:
///
///   1. Checks the action against <see cref="AutomationBoundaryPolicy"/> â€”
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
///   â€¢ Consent mode can ONLY be changed by an explicit user gesture (from Settings UI).
///   â€¢ The boundary policy check CANNOT be bypassed regardless of consent mode.
///   â€¢ Approve() / Deny() callbacks must be called from the UI thread.
///   â€¢ This service is NOT re-entrant â€” only one consent request at a time is supported.
///
/// Threading:
///   <see cref="RequestConsentAsync"/> runs on the calling thread.
///   The <see cref="ConsentRequired"/> event is raised on the UI thread via DispatcherQueue.
///   Approve/Deny callbacks unblock the awaiter from the UI thread.
/// </summary>
public sealed class AutomationConsentService
{
    // â”€â”€ Dependencies â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private readonly ITimelineAggregationService  _timeline;
    private readonly AutomationAuditService       _audit;
    private readonly ConsentExplanationService    _explanations;
    private readonly AutomationTransparencyEngine _transparency;
    private readonly IUiDispatcher _uiDispatcher;

    // â”€â”€ State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private TrustConsentMode _mode = TrustConsentMode.AskForMediumAndHighRisk;

    // â”€â”€ Constructor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    internal AutomationConsentService(
        ITimelineAggregationService  timeline,
        AutomationAuditService       audit,
        ConsentExplanationService    explanations,
        AutomationTransparencyEngine transparency,
        IUiDispatcher uiDispatcher)
    {
        _timeline     = timeline;
        _audit        = audit;
        _explanations = explanations;
        _transparency = transparency;
        _uiDispatcher = uiDispatcher;
    }

    // â”€â”€ Mode management â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Core consent gate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Evaluates whether the given step requires consent, requests it if needed,
    /// and returns true if the step is approved to execute.
    ///
    /// Called by <see cref="AutomationOrchestrator"/> before each step's execution gate.
    ///
    /// Returns:
    ///   true  â€” step is approved (either auto-approved or user-confirmed)
    ///   false â€” step was denied, blocked, or timed out
    /// </summary>
    public async Task<bool> RequestConsentAsync(
        AutomationStep   step,
        string           workflowId,
        string?          activeInsight = null,
        string?          attributedProcess = null,
        CancellationToken ct = default)
    {
        // â”€â”€ 1. Boundary policy check (unconditional) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ 2. Determine scope & risk â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var scope = MapActionIdToScope(step.ActionId, step.Risk);
        var risk  = MapAutomationRiskToTrust(step.Risk);

        // â”€â”€ 4. Observe-only / guided: always deny execution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (_mode == TrustConsentMode.ObserveOnly || _mode == TrustConsentMode.GuidedOnly)
        {
            _audit.RecordDenied(step, workflowId, scope,
                reason: $"Consent mode is '{_mode}' â€” action was described but not executed.");
            return false;
        }

        // consent gate after observe-only / guided hard-stop
        if (!RequiresConsent(risk))
        {
            // Auto-approved - log for audit trail but no UI interaction needed
            _audit.RecordAutoApproved(step, workflowId, scope);
            return true;
        }

        // consent request build
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

        // â”€â”€ 6. Show consent card and wait â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
        bool gotResponse;
        try
        {
            gotResponse = await gate.WaitAsync(TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            gotResponse = false;
        }

        if (!gotResponse || ct.IsCancellationRequested)
        {
            // Timed out or cancelled â€” treat as denial
            approved = false;
        }

        // â”€â”€ 7. Record response and publish timeline event â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

    // â”€â”€ RequiresConsent logic â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        _                                        => true,    // unknown mode â†’ always ask
    };

    // â”€â”€ Scope / risk mapping â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Timeline / dispatch â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
            _uiDispatcher.TryEnqueue(action);
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
