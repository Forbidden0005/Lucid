namespace Lucid.Services.Infrastructure.Startup;

/// <summary>
/// Immutable record of the outcome of a startup initialization step.
/// Used by <see cref="StartupTimeoutGuard"/> and the diagnostics page to
/// surface initialization failures without crashing the application.
/// </summary>
public sealed record StartupHealthSnapshot(
    string   StepName,
    bool     Succeeded,
    TimeSpan Elapsed,
    string?  ErrorMessage = null)
{
    /// <summary>UTC time this snapshot was captured.</summary>
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
}
