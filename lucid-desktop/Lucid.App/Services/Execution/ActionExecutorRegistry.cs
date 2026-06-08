namespace Lucid.Services.Execution;

/// <summary>
/// Maps action IDs to their registered <see cref="IActionExecutor"/> implementations.
///
/// Populated during application startup — before any execution is requested —
/// by calling <see cref="Register"/> or <see cref="RegisterAll"/> for each
/// concrete executor. Executors are permanent for the application lifetime;
/// there is no unregister operation.
///
/// Scale plan:
///   Executors will be added incrementally as capabilities are implemented:
///   Phase 1 — cleanup (temp files, recycle bin, Storage Sense)
///   Phase 2 — startup management (enable/disable startup entries)
///   Phase 3 — repair commands (SFC, DISM, network reset)
///   Phase 4 — uninstall flows (Store apps, Win32 via Add/Remove Programs)
///
/// Thread-safety:
///   <see cref="Register"/> and <see cref="RegisterAll"/> are intended for the
///   single-threaded startup path only. All reads (lookups, IsRegistered) are
///   safe for concurrent callers once startup is complete.
/// </summary>
public sealed class ActionExecutorRegistry
{
    private readonly Dictionary<string, IActionExecutor> _executors =
        new(StringComparer.OrdinalIgnoreCase);

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <paramref name="executor"/> for its declared
    /// <see cref="IActionExecutor.ActionId"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="executor"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An executor for the same action ID is already registered.
    /// Each action ID maps to exactly one executor.
    /// </exception>
    public void Register(IActionExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        ValidateMetadataContract(executor);

        if (_executors.ContainsKey(executor.ActionId))
            throw new InvalidOperationException(
                $"An executor for action '{executor.ActionId}' is already registered. " +
                $"Each action ID must map to exactly one executor.");

        _executors[executor.ActionId] = executor;
    }

    /// <summary>
    /// Registers multiple executors at once.
    /// Convenience wrapper over <see cref="Register"/>; stops on the first
    /// duplicate and throws.
    /// </summary>
    public void RegisterAll(IEnumerable<IActionExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        foreach (var executor in executors)
            Register(executor);
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> and sets <paramref name="executor"/>
    /// when a registered executor exists for <paramref name="actionId"/>.
    /// The lookup is case-insensitive.
    /// </summary>
    public bool TryGetExecutor(string actionId, out IActionExecutor? executor) =>
        _executors.TryGetValue(actionId, out executor);

    /// <summary>
    /// Returns <see langword="true"/> when an executor is registered for
    /// <paramref name="actionId"/>. Case-insensitive.
    /// </summary>
    public bool IsRegistered(string actionId) =>
        _executors.ContainsKey(actionId);

    /// <summary>
    /// Snapshot of all currently registered action IDs.
    /// Useful for diagnostics and startup validation.
    /// </summary>
    public IReadOnlyCollection<string> RegisteredActionIds =>
        _executors.Keys.ToList().AsReadOnly();

    /// <summary>Number of executors currently registered.</summary>
    public int Count => _executors.Count;

    private static void ValidateMetadataContract(IActionExecutor executor)
    {
        var metadata = ActionExecutionMetadataCatalog.GetRequired(executor.ActionId);

        if (metadata.RequiredPrivilege != executor.RequiredPrivilege)
            throw new InvalidOperationException(
                $"Executor '{executor.ActionId}' declares privilege '{executor.RequiredPrivilege}' " +
                $"but metadata declares '{metadata.RequiredPrivilege}'.");

        if (metadata.RequiresConfirmation != executor.RequiresConfirmation)
            throw new InvalidOperationException(
                $"Executor '{executor.ActionId}' declares RequiresConfirmation={executor.RequiresConfirmation} " +
                $"but metadata declares {metadata.RequiresConfirmation}.");

        if (metadata.SupportsDryRun != executor.SupportsDryRun)
            throw new InvalidOperationException(
                $"Executor '{executor.ActionId}' declares SupportsDryRun={executor.SupportsDryRun} " +
                $"but metadata declares {metadata.SupportsDryRun}.");

        if (metadata.SupportsRollback != executor.SupportsRollback)
            throw new InvalidOperationException(
                $"Executor '{executor.ActionId}' declares SupportsRollback={executor.SupportsRollback} " +
                $"but metadata declares {metadata.SupportsRollback}.");

        if (string.IsNullOrWhiteSpace(metadata.ConsentNote))
            throw new InvalidOperationException(
                $"Executor '{executor.ActionId}' is missing consent metadata.");

        if (string.IsNullOrWhiteSpace(metadata.FailureModeSummary))
            throw new InvalidOperationException(
                $"Executor '{executor.ActionId}' is missing failure-mode metadata.");

        if (string.IsNullOrWhiteSpace(metadata.DiagnosticsCategory))
            throw new InvalidOperationException(
                $"Executor '{executor.ActionId}' is missing diagnostics-category metadata.");
    }
}
