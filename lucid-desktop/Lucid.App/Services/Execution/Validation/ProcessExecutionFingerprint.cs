namespace Lucid.Services.Execution.Validation;

/// <summary>
/// Immutable snapshot of a process's OS-verified identity, captured
/// by <see cref="ProcessIdentityValidator"/> at the moment of validation.
///
/// Used to detect PID reuse between the time a termination was requested
/// and the time it is executed.
/// </summary>
public sealed record ProcessExecutionFingerprint(
    int      ProcessId,
    string   ActualProcessName,
    string   MainModulePath,
    DateTime CapturedAtUtc);
