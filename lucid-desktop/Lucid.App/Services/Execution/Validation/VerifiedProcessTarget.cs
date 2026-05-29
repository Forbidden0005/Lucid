namespace Lucid.Services.Execution.Validation;

/// <summary>
/// Result of OS-level process identity verification.
///
/// Contains both the verified identity and whether it matches
/// what the caller expected, allowing executors to abort
/// if the PID has been recycled by a different process.
/// </summary>
public sealed record VerifiedProcessTarget(
    int    ProcessId,
    string VerifiedName,
    string MainModulePath,
    bool   MatchesExpectedName)
{
    /// <summary>
    /// True when the caller-supplied name and the OS-verified name agree
    /// and the process is still alive.
    /// </summary>
    public bool IsConfirmed => MatchesExpectedName;
}
