namespace ExplainMyPC.Services.Behavior;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Contract for the behavioral context engine.
/// Consumed by ExplainMyPcEngine, OperationalNarrativeEngine, and ViewModels.
/// </summary>
public interface IBehavioralContextEngine
{
    /// <summary>Builds a full behavioral context snapshot from current state.</summary>
    BehavioralContext GetCurrentContext();

    /// <summary>
    /// Returns true when a heavy recommendation action should be deferred
    /// because the current workload is not a good time to run it.
    /// </summary>
    bool ShouldDeferRecommendation(string actionId, WorkloadType currentWorkload);

    /// <summary>
    /// Returns a one-paragraph contextual narrative that enriches system
    /// explanations with behavioral identity context.
    /// </summary>
    string GetContextNarrative();
}

// ── Implementation ─────────────────────────────────────────────────────────────

/// <summary>
/// Provides a unified behavioral context snapshot to other services and ViewModels.
///
/// Combines the current workload classification, machine identity, detected patterns,
/// and recent sessions into a single <see cref="BehavioralContext"/> that is
/// immutable and safe to pass across threads.
///
/// Also provides workload-aware deferral logic so the recommendation engine can
/// avoid surfacing disruptive operations during gaming or rendering sessions.
///
/// All methods are safe to call from any thread (reads only, no mutations).
/// </summary>
public sealed class BehavioralContextEngine : IBehavioralContextEngine
{
    // ── Actions that bypass workload deferral (always appropriate) ────────────

    private static readonly HashSet<string> s_alwaysOk = new(StringComparer.OrdinalIgnoreCase)
    {
        "action.cpu.open-task-manager",
        "action.security.open-windows-security",
        "action.process.open-location",
    };

    // ── Workloads where disruptive background actions should be deferred ───────

    private static readonly HashSet<WorkloadType> s_noDisrupt = new()
    {
        WorkloadType.Gaming,
        WorkloadType.Rendering,
        WorkloadType.MediaEditing,
    };

    // ── Heavy actions that should be deferred during disruptive workloads ─────

    private static readonly HashSet<string> s_heavyActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "action.repair.sfc-scannow",
        "action.repair.dism-restore-health",
        "action.disk.clean-temp-files",
        "action.disk.empty-recycle-bin",
        "action.disk.clean-windows-update-cache",
        "action.disk.clean-delivery-optimization",
        "action.disk.clean-browser-cache",
        "action.storage.delete-large-file",
        "action.storage.delete-duplicate-group",
        "action.storage.clean-old-downloads",
    };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly WorkloadProfilingService _profiling;

    // ── Construction ──────────────────────────────────────────────────────────

    public BehavioralContextEngine(WorkloadProfilingService profiling)
        => _profiling = profiling;

    // ── IBehavioralContextEngine ──────────────────────────────────────────────

    /// <inheritdoc />
    public BehavioralContext GetCurrentContext()
    {
        // Build a placeholder classification if profiling hasn't run yet
        var classification = _profiling.CurrentClassification
            ?? new WorkloadClassification(
                WorkloadType.Unknown, WorkloadType.Unknown,
                BehaviorConfidence.Low,
                "Workload profiling is still warming up.",
                DateTimeOffset.Now, []);

        return new BehavioralContext(
            CurrentWorkload:    classification,
            MachineIdentity:    _profiling.MachineIdentity,
            ActivePatterns:     _profiling.DetectedPatterns,
            RecentSessions:     _profiling.RecentSessions,
            CurrentFingerprint: _profiling.CurrentFingerprint,
            ContextAt:          DateTimeOffset.Now);
    }

    /// <inheritdoc />
    public bool ShouldDeferRecommendation(string actionId, WorkloadType currentWorkload)
    {
        if (s_alwaysOk.Contains(actionId))     return false;
        if (!s_heavyActions.Contains(actionId)) return false;
        return s_noDisrupt.Contains(currentWorkload);
    }

    /// <inheritdoc />
    public string GetContextNarrative()
    {
        var classification = _profiling.CurrentClassification;
        if (classification is null)
            return "Behavioral profiling is building — check back after the system has been observed for a few minutes.";

        var identity = _profiling.MachineIdentity;
        string current = WorkloadLabel(classification.PrimaryWorkload);

        // Describe whether the current workload is typical for this machine
        bool isExpected = identity.DominantWorkload  == classification.PrimaryWorkload ||
                          identity.SecondaryWorkload == classification.PrimaryWorkload;

        string contextSentence;
        if (identity.DominantWorkload == WorkloadType.Unknown)
        {
            contextSentence = "Behavioral identity is still being established.";
        }
        else if (isExpected)
        {
            contextSentence = $"This is a typical {current} session — consistent with this machine's behavioral profile.";
        }
        else
        {
            string domLabel = WorkloadLabel(identity.DominantWorkload);
            contextSentence = $"This {current} workload is less common on this machine, which primarily runs {domLabel} workloads.";
        }

        return classification.Rationale + " " + contextSentence;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string WorkloadLabel(WorkloadType t) => t switch
    {
        WorkloadType.Gaming              => "gaming",
        WorkloadType.Development         => "development",
        WorkloadType.MediaEditing        => "media editing",
        WorkloadType.Rendering           => "rendering",
        WorkloadType.Streaming           => "streaming",
        WorkloadType.BrowserResearch     => "browser/research",
        WorkloadType.Productivity        => "productivity",
        WorkloadType.BackgroundProcessing => "background processing",
        WorkloadType.StartupHeavy        => "startup-heavy",
        WorkloadType.ThermalIntensive    => "thermal-intensive",
        WorkloadType.OvernightProcessing => "overnight",
        WorkloadType.Idle                => "idle",
        _                               => "unrecognized",
    };
}
