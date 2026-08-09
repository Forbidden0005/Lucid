using Lucid.Services.Governance;

namespace Lucid.Services.Execution;

/// <summary>
/// Transparent decorator around <see cref="IActionExecutionEngine"/> that
/// consults the concurrency budget before dispatching heavy executors.
///
/// Lightweight, navigation-only actions (Open Task Manager, Open Storage Sense,
/// etc.) bypass governance entirely — they carry no system cost and must never
/// be blocked. Heavy executors that run DISM, SFC, file I/O, or process
/// termination are classified as <see cref="WorkloadCategory.ActionExecution"/>
/// and must obtain a budget slot before starting.
///
/// When the budget refuses a slot, <see cref="ExecuteAsync"/> returns a
/// <see cref="ActionExecutionStatus.Failed"/> result with a clear explanation
/// rather than blocking the caller.
///
/// Usage:
///   Replace <see cref="ActionExecutionEngine"/> with this class in
///   The composition root. Callers hold the <see cref="IActionExecutionEngine"/>
///   interface and are unaware of the governance layer.
/// </summary>
public sealed class GovernanceAwareExecutionEngine : IActionExecutionEngine
{
    // ── Action IDs that are lightweight UI navigation — never blocked ─────────

    private static readonly HashSet<string> NavigationOnlyActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "action.cpu.open-task-manager",
        "action.disk.run-storage-sense",
        "action.startup.open-startup-apps",
        "action.security.open-windows-security",
        "action.process.open-location",
        "action.security.open-virustotal",
    };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IActionExecutionEngine      _inner;
    private readonly IRuntimeGovernanceService   _governance;

    /// <summary>
    /// Optional callback invoked after each execution with (actionId, success, errorDetail).
    /// Used by the diagnostics layer to track executor health without creating a direct dependency.
    /// Never throws — exceptions from the callback are swallowed.
    /// </summary>
    private readonly Action<string, bool, string?>? _onExecutionResult;

    // ── Construction ──────────────────────────────────────────────────────────

    public GovernanceAwareExecutionEngine(
        IActionExecutionEngine      inner,
        IRuntimeGovernanceService   governance,
        Action<string, bool, string?>? onExecutionResult = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(governance);

        _inner             = inner;
        _governance        = governance;
        _onExecutionResult = onExecutionResult;
    }

    // ── IActionExecutionEngine ────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ActionExecutionResult> ExecuteAsync(
        string                actionId,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        // Navigation-only or unrecognised actions bypass governance.
        if (NavigationOnlyActions.Contains(actionId))
            return await _inner.ExecuteAsync(actionId, context, cancellationToken).ConfigureAwait(false);

        // Heavy actions must acquire a concurrency slot.
        string workloadName = $"action:{actionId}";

        if (!_governance.TryAcquireSlot(
                WorkloadCategory.ActionExecution,
                workloadName,
                out string? refusalReason))
        {
            string detail = refusalReason ?? "A concurrency slot could not be obtained at this time.";
            context.Log.Error($"[Governance] Execution blocked: {detail}");

            return ActionExecutionResult.Failed(
                actionId,
                message:     "Execution deferred — system is under load.",
                duration:    TimeSpan.Zero,
                log:         context.Log.Build(),
                errorDetail: detail);
        }

        try
        {
            var result = await _inner.ExecuteAsync(actionId, context, cancellationToken).ConfigureAwait(false);

            // Notify diagnostics callback (fire-and-forget, never throws).
            try
            {
                bool success = result.IsSuccess || result.IsDryRun;
                _onExecutionResult?.Invoke(actionId, success,
                    success ? null : result.ErrorDetail ?? result.Message);
            }
            catch { /* diagnostics callback must never break execution */ }

            return result;
        }
        finally
        {
            _governance.ReleaseSlot(WorkloadCategory.ActionExecution, workloadName);
        }
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> RollbackAsync(
        string                actionId,
        string                rollbackToken,
        ActionExecutionContext context,
        CancellationToken     cancellationToken = default)
    {
        // Rollbacks are user-initiated and time-sensitive — treat as foreground,
        // bypass governance so a rollback is never blocked by a background task.
        return await _inner.RollbackAsync(actionId, rollbackToken, context, cancellationToken)
                           .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsSupported(string actionId) => _inner.IsSupported(actionId);
}
