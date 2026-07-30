namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// Determines the display priority and action urgency of an
/// <see cref="OperationalInference"/> produced by the cognitive reasoning engine.
///
/// Priority is computed from the combination of confidence, impact, and current
/// operational context — NOT from raw metric thresholds alone.
///
/// Examples:
///   Thermal spikes during video rendering → <see cref="Informational"/> (expected)
///   Thermal spikes during idle            → <see cref="Elevated"/> (unexpected)
///   Thermal runaway forecast              → <see cref="Critical"/>
/// </summary>
public enum InferencePriority
{
    /// <summary>Background observation — no user action needed. Logged for history.</summary>
    Background = 0,

    /// <summary>Noteworthy but expected given the current workload context.</summary>
    Informational = 1,

    /// <summary>Unexpected or suboptimal — worth reviewing when convenient.</summary>
    Advisory = 2,

    /// <summary>Operationally significant — should be reviewed soon.</summary>
    Elevated = 3,

    /// <summary>Stability risk or resource exhaustion trajectory — act now.</summary>
    Critical = 4,
}

public static class InferencePriorityExtensions
{
    public static string ToLabel(this InferencePriority p) => p switch
    {
        InferencePriority.Background    => "Background",
        InferencePriority.Informational => "Informational",
        InferencePriority.Advisory      => "Advisory",
        InferencePriority.Elevated      => "Elevated",
        InferencePriority.Critical      => "Critical",
        _                               => "Unknown",
    };

    public static string ToColor(this InferencePriority p) => p switch
    {
        InferencePriority.Critical      => "#F87171",
        InferencePriority.Elevated      => "#FBBF24",
        InferencePriority.Advisory      => "#60A5FA",
        InferencePriority.Informational => "#94A3B8",
        InferencePriority.Background    => "#4B5563",
        _                               => "#6B7280",
    };
}
