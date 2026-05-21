namespace ExplainMyPC.Services.Governance;

/// <summary>
/// Pure static policy table mapping <see cref="RuntimeMode"/> to concrete
/// scheduling parameters (polling intervals, concurrency budgets).
///
/// All values are conservative defaults chosen to keep ExplainMyPC invisible
/// to the user while still collecting meaningful data.
///
/// Telemetry interval by mode:
///   Normal           1.5 s  — standard rate
///   HighLoad         3.0 s  — reduce overhead while under CPU pressure
///   LowPower         6.0 s  — save battery, extend life by ~60%
///   Gaming           4.0 s  — reduce interference with frame pacing
///   ThermalProtection 2.5 s — still need thermal data to detect recovery
///
/// Process sample interval by mode (process sampling is the most expensive tick):
///   Normal            4.5 s
///   HighLoad          9.0 s
///   LowPower         20.0 s
///   Gaming           15.0 s
///   ThermalProtection 12.0 s
/// </summary>
internal static class AdaptiveSchedulingPolicy
{
    // ── Telemetry polling intervals ────────────────────────────────────────────

    private static readonly TimeSpan TelNormal    = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan TelHighLoad  = TimeSpan.FromSeconds(3.0);
    private static readonly TimeSpan TelLowPower  = TimeSpan.FromSeconds(6.0);
    private static readonly TimeSpan TelGaming    = TimeSpan.FromSeconds(4.0);
    private static readonly TimeSpan TelThermal   = TimeSpan.FromSeconds(2.5);

    // ── Process sample intervals ───────────────────────────────────────────────

    private static readonly TimeSpan ProcNormal   = TimeSpan.FromSeconds(4.5);
    private static readonly TimeSpan ProcHighLoad = TimeSpan.FromSeconds(9.0);
    private static readonly TimeSpan ProcLowPower = TimeSpan.FromSeconds(20.0);
    private static readonly TimeSpan ProcGaming   = TimeSpan.FromSeconds(15.0);
    private static readonly TimeSpan ProcThermal  = TimeSpan.FromSeconds(12.0);

    // ── Public policy accessors ────────────────────────────────────────────────

    /// <summary>Target hardware telemetry polling interval for the given mode.</summary>
    public static TimeSpan GetTelemetryInterval(RuntimeMode mode) => mode switch
    {
        RuntimeMode.HighLoad          => TelHighLoad,
        RuntimeMode.LowPower          => TelLowPower,
        RuntimeMode.Gaming            => TelGaming,
        RuntimeMode.ThermalProtection => TelThermal,
        _                             => TelNormal,
    };

    /// <summary>Process list sample interval for the given mode.</summary>
    public static TimeSpan GetProcessSampleInterval(RuntimeMode mode) => mode switch
    {
        RuntimeMode.HighLoad          => ProcHighLoad,
        RuntimeMode.LowPower          => ProcLowPower,
        RuntimeMode.Gaming            => ProcGaming,
        RuntimeMode.ThermalProtection => ProcThermal,
        _                             => ProcNormal,
    };

    /// <summary>
    /// Maximum number of background-class workloads that may run concurrently.
    /// Foreground workloads are always unconstrained.
    /// </summary>
    public static int GetMaxConcurrentBackground(RuntimeMode mode) => mode switch
    {
        RuntimeMode.LowPower          => 0,   // pause all background work
        RuntimeMode.Gaming            => 0,   // yield entirely to game workload
        RuntimeMode.ThermalProtection => 0,   // halt non-critical background tasks
        RuntimeMode.HighLoad          => 1,   // one background task at a time
        _                             => 3,   // normal: up to 3 concurrent
    };

    /// <summary>
    /// Returns true when IdleOnly workloads (storage scans, hashing, etc.)
    /// are permitted to start in the given mode.
    /// </summary>
    public static bool AllowIdleOnlyWork(RuntimeMode mode) =>
        mode == RuntimeMode.Normal;

    /// <summary>
    /// Returns a human-readable description of why background work is
    /// paused in the given mode.
    /// </summary>
    public static string GetDeferralDescription(RuntimeMode mode) => mode switch
    {
        RuntimeMode.LowPower          => "Background analysis paused to conserve battery.",
        RuntimeMode.Gaming            => "Background analysis paused — gaming workload detected.",
        RuntimeMode.ThermalProtection => "Background analysis halted — thermal protection active.",
        RuntimeMode.HighLoad          => "Background analysis reduced — CPU under high load.",
        _                             => "Background analysis running normally.",
    };
}
