namespace ExplainMyPC.Services.Execution;

/// <summary>
/// Execution context created by <see cref="ActionExecutionEngine"/> for each
/// invocation of <see cref="IActionExecutionEngine.ExecuteAsync"/> or
/// <see cref="IActionExecutionEngine.RollbackAsync"/>.
///
/// Carries user intent (dry-run, confirmation), security posture (elevation),
/// and the mutable log builder. Executors read from the context and write to
/// <see cref="Log"/>; they must not cache or share the context instance.
///
/// Design:
///   • Never reused across calls — one context per engine invocation.
///   • All properties are set via init-only setters so the engine can
///     build the context with an object initialiser without exposing setters
///     to executor code.
///   • The engine itself creates the context; the ViewModel layer is
///     responsible for populating the intent flags (IsDryRun, Confirmation)
///     from user actions before calling the engine.
/// </summary>
public sealed class ActionExecutionContext
{
    /// <summary>
    /// When <see langword="true"/> the executor must simulate all side effects
    /// without making any persistent changes, then log what it would have done.
    ///
    /// The engine pre-checks <see cref="IActionExecutor.SupportsDryRun"/> and
    /// returns a synthetic <see cref="ActionExecutionStatus.DryRunSuccess"/>
    /// before calling the executor when this is <see langword="true"/> and the
    /// executor does not support dry-run.
    /// </summary>
    public bool IsDryRun { get; init; }

    /// <summary>
    /// <see langword="true"/> when the current process was launched with UAC
    /// elevation (administrator). Populated once at startup by a privilege
    /// check and passed into every context.
    ///
    /// The engine returns <see cref="ActionExecutionStatus.RequiresElevation"/>
    /// before calling the executor when this is <see langword="false"/> and the
    /// executor declares <see cref="ActionPrivilegeLevel.Administrator"/>.
    /// </summary>
    public bool IsElevated { get; init; }

    /// <summary>
    /// <see langword="true"/> when the user has explicitly acknowledged the
    /// confirmation dialog for a risky action.
    ///
    /// The ViewModel layer is responsible for showing the confirmation UI and
    /// setting this flag before calling the engine. The engine then enforces it
    /// for every executor that declares <see cref="IActionExecutor.RequiresConfirmation"/>
    /// = <see langword="true"/>, returning
    /// <see cref="ActionExecutionStatus.RequiresConfirmation"/> if this flag is
    /// <see langword="false"/>.
    /// </summary>
    public bool ConfirmationGranted { get; init; }

    /// <summary>
    /// Human-readable label for whoever initiated this execution.
    /// Written into the log's dispatch entry. Defaults to <c>"user"</c>.
    /// Use <c>"system"</c> for engine-initiated background actions.
    /// </summary>
    public string RequestedBy { get; init; } = "user";

    /// <summary>
    /// Mutable log the engine and executor both write progress messages into.
    ///
    /// The engine writes a dispatch entry before calling the executor.
    /// The executor writes its own step-by-step entries.
    /// Both the executor (on success) and the engine (on exception) call
    /// <see cref="ActionExecutionLog.Build"/> exactly once to seal the entries
    /// into the <see cref="ActionExecutionResult"/>.
    ///
    /// Never null — always initialised to a fresh empty log.
    ///
    /// The ViewModel layer should supply a pre-configured log that includes a
    /// live-streaming callback so log entries appear in the UI in real time:
    /// <code>
    ///   var context = new ActionExecutionContext
    ///   {
    ///       Log = new ActionExecutionLog(entry => dispatcher.TryEnqueue(
    ///                 () => LogEntries.Add(new ActionLogEntryViewModel(entry)))),
    ///       ...
    ///   };
    /// </code>
    /// </summary>
    public ActionExecutionLog Log { get; init; } = new();
}
