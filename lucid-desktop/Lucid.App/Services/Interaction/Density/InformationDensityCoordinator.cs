using Lucid.Services.Infrastructure.OperationalState;
using Lucid.Services.Reasoning.Cognitive;

namespace Lucid.Services.Interaction.Density;

/// <summary>
/// Manages the current information density level for the Lucid UI.
///
/// The coordinator tracks the active density state and exposes it for
/// ViewModels to query. Density is adjusted:
///   - Automatically: when system pressure changes (via Refresh())
///   - Manually: when the user explicitly enters a Diagnostic panel
///
/// The user's explicit density selection always wins over the automatic estimate.
/// Automatic adjustment resumes when the user returns to the default panel.
///
/// Thread-safe: volatile field for current level, explicit reset method.
/// </summary>
public sealed class InformationDensityCoordinator
{
    private readonly IOperationalStateCoordinator  _operationalState;
    private readonly ICognitiveReasoningEngine     _cognitiveEngine;

    // Current automatic estimate (updated by Refresh).
    private volatile int _currentLevelInt = (int)InformationDensityLevel.Standard;

    // Whether the user has manually overridden the density.
    private volatile bool _isManualOverride;
    private volatile int  _overrideLevelInt = (int)InformationDensityLevel.Standard;

    public InformationDensityCoordinator(
        IOperationalStateCoordinator operationalState,
        ICognitiveReasoningEngine    cognitiveEngine)
    {
        _operationalState = operationalState;
        _cognitiveEngine  = cognitiveEngine;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The currently recommended information density level.
    /// When a user override is active, the override wins.
    /// </summary>
    public InformationDensityLevel CurrentLevel =>
        _isManualOverride
            ? (InformationDensityLevel)_overrideLevelInt
            : (InformationDensityLevel)_currentLevelInt;

    /// <summary>True when the user has manually set a density level.</summary>
    public bool IsManualOverride => _isManualOverride;

    /// <summary>
    /// Re-estimates density from current system state.
    /// Call after each cognitive reasoning cycle.
    /// </summary>
    public void Refresh()
    {
        var state          = _operationalState.Current;
        var activeInferences = _cognitiveEngine.CurrentInferences
            .Where(i => !i.IsSuppressed)
            .ToList()
            .AsReadOnly();

        var load  = CognitiveLoadEstimator.Estimate(state, activeInferences);
        var level = CognitiveLoadEstimator.ToDensityLevel(load);

        Interlocked.Exchange(ref _currentLevelInt, (int)level);
    }

    /// <summary>
    /// Sets a manual density override. The automatic estimate is suspended
    /// until <see cref="ClearOverride"/> is called.
    /// </summary>
    public void SetOverride(InformationDensityLevel level)
    {
        Interlocked.Exchange(ref _overrideLevelInt, (int)level);
        _isManualOverride = true;
    }

    /// <summary>Clears any manual override and resumes automatic density estimation.</summary>
    public void ClearOverride()
    {
        _isManualOverride = false;
    }
}
