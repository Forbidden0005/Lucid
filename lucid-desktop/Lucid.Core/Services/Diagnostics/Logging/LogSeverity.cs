namespace Lucid.Services.Diagnostics.Logging;

/// <summary>
/// Operational log severity levels used by <see cref="IOperationalLogger"/>.
///
/// Maps directly to <see cref="Lucid.Core.Infrastructure.LogLevel"/> for
/// writing to the file sink via <see cref="Lucid.Core.Infrastructure.ILucidLogger"/>.
/// </summary>
public enum LogSeverity
{
    /// <summary>Verbose diagnostic information — development and tracing only.</summary>
    Debug = 0,

    /// <summary>Expected operational milestones (service started, workflow completed, etc.).</summary>
    Info = 1,

    /// <summary>Unexpected but recoverable conditions worth investigating.</summary>
    Warning = 2,

    /// <summary>Service-level failures that affect a feature but not platform stability.</summary>
    Error = 3,

    /// <summary>Platform-wide failures that require immediate attention.</summary>
    Critical = 4,
}
