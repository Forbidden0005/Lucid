namespace Lucid.Services.Reasoning.Context;

/// <summary>
/// Describes what the system is currently doing and how to interpret
/// resource usage within that context.
///
/// Context profiles are the core of Lucid's contextual intelligence:
/// a 90% CPU during a game compile is EXPECTED behavior;
/// the same reading during idle is an anomaly worth investigating.
///
/// Produced by <see cref="IOperationalContextSynthesizer"/> and consumed
/// by the cognitive reasoning engine to adjust inference confidence and
/// suppress contextually appropriate findings.
///
/// Immutable — create a new profile each synthesis cycle.
/// </summary>
public sealed record ContextProfile
{
    /// <summary>UTC time this context profile was computed.</summary>
    public DateTime ComputedAt { get; init; } = DateTime.UtcNow;

    // ── Classification ────────────────────────────────────────────────────────

    /// <summary>
    /// Primary activity classification for the current session.
    /// Drives suppression rules in the reasoning engine.
    /// </summary>
    public ActivityClass PrimaryActivity { get; init; } = ActivityClass.Unknown;

    /// <summary>
    /// Confidence in the primary activity classification (0–1).
    /// Low confidence classifications are used cautiously.
    /// </summary>
    public double ClassificationConfidence { get; init; }

    /// <summary>
    /// Human-readable workload label for UI display and LLM context injection.
    /// Examples: "Gaming", "Compiling code", "Watching video", "System idle".
    /// </summary>
    public string WorkloadLabel { get; init; } = "Unknown workload";

    // ── Expected resource baselines for this context ──────────────────────────

    /// <summary>Expected CPU range (%) for this activity class. Used to assess anomalies.</summary>
    public (double Low, double High) ExpectedCpuRange { get; init; }

    /// <summary>Expected RAM range (%) for this activity class.</summary>
    public (double Low, double High) ExpectedRamRange { get; init; }

    /// <summary>Expected thermal range (°C) for this activity class.</summary>
    public (double Low, double High) ExpectedThermalRange { get; init; }

    // ── Suppression hints ─────────────────────────────────────────────────────

    /// <summary>
    /// True if high CPU is expected and normal in this context.
    /// The reasoning engine will suppress CPU-related inferences when this is true.
    /// </summary>
    public bool HighCpuIsExpected { get; init; }

    /// <summary>True if elevated disk I/O is expected in this context (e.g., updates, backups).</summary>
    public bool HighDiskIoIsExpected { get; init; }

    /// <summary>True if thermal elevation is expected and normal in this context.</summary>
    public bool ThermalElevationIsExpected { get; init; }

    /// <summary>
    /// True if the system is in a post-boot window where transient resource pressure
    /// is expected as startup services initialize.
    /// </summary>
    public bool IsPostBootTransient { get; init; }

    // ── Signal provenance ─────────────────────────────────────────────────────

    /// <summary>
    /// Names of active processes that drove this classification.
    /// Used for explainability ("Gaming detected because Steam and game process are running").
    /// Never includes user document names or personal paths — process names only.
    /// </summary>
    public IReadOnlyList<string> DriverProcessNames { get; init; } = [];

    /// <summary>
    /// Whether a Windows Update or system maintenance task is actively running.
    /// When true, disk/CPU pressure from system processes is contextually expected.
    /// </summary>
    public bool WindowsUpdateActive { get; init; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given CPU percent falls within the expected range
    /// for this context, with an optional tolerance margin.
    /// </summary>
    public bool IsCpuExpected(double cpuPercent, double tolerancePercent = 10.0) =>
        cpuPercent >= ExpectedCpuRange.Low - tolerancePercent &&
        cpuPercent <= ExpectedCpuRange.High + tolerancePercent;

    /// <summary>Returns a plain-English statement about whether the current load is contextually normal.</summary>
    public string NormalityStatement => PrimaryActivity switch
    {
        ActivityClass.Gaming          => "High resource usage is expected during gaming.",
        ActivityClass.Compiling       => "High CPU is expected during compilation.",
        ActivityClass.VideoStreaming  => "Moderate CPU and GPU usage is expected during streaming.",
        ActivityClass.SystemUpdate    => "High disk and CPU activity is expected during updates.",
        ActivityClass.Rendering       => "High CPU and GPU usage is expected during rendering.",
        ActivityClass.Backup          => "High disk I/O is expected during backup operations.",
        ActivityClass.Idle            => "Resource usage should be low while the system is idle.",
        _                             => "Workload context is not yet established.",
    };
}

/// <summary>
/// Classified activity type for the current operational session.
/// </summary>
public enum ActivityClass
{
    /// <summary>Activity has not been classified yet.</summary>
    Unknown = 0,

    /// <summary>System is effectively idle — no significant user-initiated workload.</summary>
    Idle = 1,

    /// <summary>Gaming session detected (game process + high GPU + game audio).</summary>
    Gaming = 2,

    /// <summary>Build or compilation workload (compiler processes, high sustained CPU).</summary>
    Compiling = 3,

    /// <summary>Video streaming or playback (browser/media app + GPU decode).</summary>
    VideoStreaming = 4,

    /// <summary>3D rendering or video encoding (high CPU + GPU over extended duration).</summary>
    Rendering = 5,

    /// <summary>Windows Update or system maintenance actively running.</summary>
    SystemUpdate = 6,

    /// <summary>Backup software running (high disk I/O, backup process names).</summary>
    Backup = 7,

    /// <summary>General productivity (Office, IDE, browser — moderate mixed load).</summary>
    Productivity = 8,

    /// <summary>Browser-heavy session (multiple browser windows, high RAM, moderate CPU).</summary>
    BrowsingHeavy = 9,

    /// <summary>Mixed or unclear workload that doesn't fit a dominant category.</summary>
    Mixed = 10,
}
