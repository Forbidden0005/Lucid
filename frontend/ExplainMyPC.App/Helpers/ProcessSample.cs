namespace ExplainMyPC.Helpers;

/// <summary>
/// Immutable snapshot of a single process captured during a telemetry poll cycle.
///
/// CPU percentage is normalised across all logical cores so values are always
/// in the [0, 100] range regardless of core count — 100 % means the process
/// is consuming one full core's worth of CPU time on the entire machine.
///
/// RAM is the process Working Set (physical RAM currently in use).
///
/// Produced exclusively by <see cref="Telemetry.Samplers.ProcessSampler"/> and
/// surfaced through <see cref="TelemetrySnapshot.TopProcesses"/>.
/// </summary>
public sealed record ProcessSample(
    /// <summary>OS-assigned process identifier.</summary>
    int    ProcessId,

    /// <summary>
    /// Raw executable name without extension, as returned by
    /// <see cref="System.Diagnostics.Process.ProcessName"/>.
    /// E.g. "chrome", "msedge", "obs64".
    /// </summary>
    string ProcessName,

    /// <summary>
    /// Human-friendly mapped name, e.g. "Google Chrome", "Microsoft Edge".
    /// Falls back to Title-Cased ProcessName for unknown processes.
    /// </summary>
    string DisplayName,

    /// <summary>
    /// CPU usage as a percentage of all logical cores combined [0, 100].
    /// Requires two consecutive samples; always 0 on the first poll cycle.
    /// </summary>
    float  CpuPercent,

    /// <summary>Working Set in bytes (physical RAM currently committed).</summary>
    long   RamBytes);
