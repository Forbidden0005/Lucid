namespace ExplainMyPC.Services.Execution;

// ── Log level ─────────────────────────────────────────────────────────────────

/// <summary>Severity tier for an entry in an action execution log.</summary>
public enum ActionLogLevel
{
    /// <summary>Normal progress or diagnostic information.</summary>
    Info    = 0,

    /// <summary>A non-fatal concern that did not stop execution.</summary>
    Warning = 1,

    /// <summary>An error that caused a step to fail or execution to abort.</summary>
    Error   = 2,
}

// ── Log entry ─────────────────────────────────────────────────────────────────

/// <summary>
/// A single timestamped message produced during an action execution.
/// Immutable value object — written once and never mutated.
/// </summary>
public sealed record ActionLogEntry(
    DateTimeOffset Timestamp,
    ActionLogLevel Level,
    string         Message);

// ── Log builder ───────────────────────────────────────────────────────────────

/// <summary>
/// Mutable log builder carried by <see cref="ActionExecutionContext"/> through
/// the engine and into each executor.
///
/// Lifecycle:
///   The engine creates a fresh instance per <see cref="IActionExecutionEngine.ExecuteAsync"/>
///   call and attaches it to the context. The engine appends a dispatch entry,
///   then the executor appends its own entries throughout execution. When the
///   executor (or, on exception, the engine) finishes building an
///   <see cref="ActionExecutionResult"/>, it calls <see cref="Build"/> once to
///   seal the accumulated entries into the immutable result.
///
/// Thread-safety:
///   Not thread-safe. Executors are expected to write sequentially on a single
///   logical execution path. Parallel sub-tasks should serialise their log writes.
/// </summary>
public sealed class ActionExecutionLog
{
    private readonly List<ActionLogEntry>   _entries = new();
    private readonly Action<ActionLogEntry>? _onEntryAdded;

    /// <summary>
    /// Creates a log builder with no live-streaming callback.
    /// Entries accumulate silently until <see cref="Build"/> is called.
    /// </summary>
    public ActionExecutionLog() { }

    /// <summary>
    /// Creates a log builder that invokes <paramref name="onEntryAdded"/>
    /// synchronously on the writing thread each time an entry is appended.
    ///
    /// The ViewModel passes a callback that marshals the entry to the UI
    /// thread and adds it to an <c>ObservableCollection</c>, giving the user
    /// a live view of execution progress without waiting for <see cref="Build"/>.
    /// </summary>
    public ActionExecutionLog(Action<ActionLogEntry> onEntryAdded) =>
        _onEntryAdded = onEntryAdded;

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>Appends an <see cref="ActionLogLevel.Info"/> entry.</summary>
    public void Info(string message)  => Append(ActionLogLevel.Info,    message);

    /// <summary>Appends an <see cref="ActionLogLevel.Warning"/> entry.</summary>
    public void Warn(string message)  => Append(ActionLogLevel.Warning, message);

    /// <summary>Appends an <see cref="ActionLogLevel.Error"/> entry.</summary>
    public void Error(string message) => Append(ActionLogLevel.Error,   message);

    private void Append(ActionLogLevel level, string message)
    {
        var entry = new ActionLogEntry(DateTimeOffset.Now, level, message);
        _entries.Add(entry);
        _onEntryAdded?.Invoke(entry);
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>Number of entries written so far.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Returns an immutable read-only view of all entries written so far.
    ///
    /// Called by the executor when it constructs its <see cref="ActionExecutionResult"/>,
    /// and by the engine in exception / pre-flight-failure code paths.
    ///
    /// Calling <see cref="Build"/> does not prevent further writes — subsequent
    /// entries will not appear in the previously returned snapshot but will be
    /// captured by a later call. In practice each execution path calls
    /// <see cref="Build"/> exactly once at the end.
    /// </summary>
    public IReadOnlyList<ActionLogEntry> Build() => _entries.AsReadOnly();
}
