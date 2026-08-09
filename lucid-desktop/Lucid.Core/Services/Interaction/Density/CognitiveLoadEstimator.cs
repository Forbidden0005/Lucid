using Lucid.Services.Infrastructure.OperationalState;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Interaction.Density;

/// <summary>
/// Estimates cognitive load based on operational signals — how much information
/// the user would need to process if Lucid surfaces everything it knows.
///
/// Cognitive load is determined by two factors:
///   1. System load — how busy is the machine right now?
///   2. Inference density — how many active findings are there?
///
/// High system load + many inferences = high cognitive burden.
/// The estimate informs <see cref="InformationDensityCoordinator"/> decisions.
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class CognitiveLoadEstimator
{
    /// <summary>Describes the estimated cognitive burden at a point in time.</summary>
    public enum CognitiveLoadLevel
    {
        /// <summary>Low system activity, few findings — user can absorb full detail.</summary>
        Low,

        /// <summary>Moderate activity or findings — standard density is appropriate.</summary>
        Moderate,

        /// <summary>Heavy system load or many findings — reduce information density.</summary>
        High,
    }

    /// <summary>
    /// Estimates current cognitive load from the operational state and active inferences.
    /// </summary>
    public static CognitiveLoadLevel Estimate(
        OperationalStateSnapshot          state,
        IReadOnlyList<OperationalInference> activeInferences)
    {
        var systemPressure   = ComputeSystemPressure(state);
        var inferenceDensity = ComputeInferenceDensity(activeInferences);

        // High on either dimension → constrain density.
        if (systemPressure == 2 || inferenceDensity == 2) return CognitiveLoadLevel.High;
        if (systemPressure >= 1 || inferenceDensity >= 1) return CognitiveLoadLevel.Moderate;
        return CognitiveLoadLevel.Low;
    }

    /// <summary>
    /// Maps a cognitive load level to the appropriate information density.
    /// </summary>
    public static InformationDensityLevel ToDensityLevel(CognitiveLoadLevel load) => load switch
    {
        CognitiveLoadLevel.High     => InformationDensityLevel.Compact,
        CognitiveLoadLevel.Moderate => InformationDensityLevel.Standard,
        _                           => InformationDensityLevel.Detailed,
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    // 0 = low, 1 = moderate, 2 = high
    private static int ComputeSystemPressure(OperationalStateSnapshot state)
    {
        int score = 0;
        if (state.CpuPercent > 80)            score++;
        if (state.RamPercent > 85)             score++;
        if (state.DegradedServiceCount > 0)    score++;
        if (state.ActiveWorkflowCount > 2)     score++;

        return score >= 3 ? 2 : score >= 1 ? 1 : 0;
    }

    // 0 = few, 1 = moderate, 2 = many
    private static int ComputeInferenceDensity(IReadOnlyList<OperationalInference> inferences)
    {
        var activeCount = inferences.Count(i => !i.IsSuppressed);
        return activeCount >= 5 ? 2 : activeCount >= 2 ? 1 : 0;
    }
}
