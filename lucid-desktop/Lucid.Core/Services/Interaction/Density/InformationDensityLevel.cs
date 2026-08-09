namespace Lucid.Services.Interaction.Density;

/// <summary>
/// Describes the complexity level of information to surface to the user.
///
/// Lucid adapts information density to the current context — reducing noise
/// during high system activity and expanding detail when the user investigates.
/// This prevents cognitive overload while preserving full transparency on demand.
/// </summary>
public enum InformationDensityLevel
{
    /// <summary>
    /// Minimal information — one or two key facts only.
    /// Used when: system is under heavy load, user is mid-workflow, or
    /// CognitiveLoadEstimator classifies burden as High.
    /// </summary>
    Compact = 0,

    /// <summary>
    /// Standard information — headline + brief summary.
    /// Default level for normal operating conditions.
    /// </summary>
    Standard = 1,

    /// <summary>
    /// Expanded information — headline + summary + trajectory + related context.
    /// Used when: user explicitly opens a card, or the diagnostic panel is active.
    /// </summary>
    Detailed = 2,

    /// <summary>
    /// Full diagnostic information — all evidence, confidence breakdown, rule traces.
    /// Used only in the Diagnostics and Explainability panels.
    /// Not shown in the main operational view.
    /// </summary>
    Diagnostic = 3,
}

/// <summary>Extension helpers for InformationDensityLevel.</summary>
public static class InformationDensityLevelExtensions
{
    /// <summary>Returns true when the level is at or above <paramref name="threshold"/>.</summary>
    public static bool AtLeast(this InformationDensityLevel level,
                                InformationDensityLevel     threshold) =>
        (int)level >= (int)threshold;

    /// <summary>Returns the display label for the density level.</summary>
    public static string ToLabel(this InformationDensityLevel level) => level switch
    {
        InformationDensityLevel.Compact    => "Compact",
        InformationDensityLevel.Standard   => "Standard",
        InformationDensityLevel.Detailed   => "Detailed",
        InformationDensityLevel.Diagnostic => "Diagnostic",
        _                                  => "Standard",
    };
}
