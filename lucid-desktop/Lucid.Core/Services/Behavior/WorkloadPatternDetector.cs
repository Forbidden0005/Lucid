namespace Lucid.Services.Behavior;

/// <summary>
/// Analyzes completed session history to identify recurring behavioral patterns.
///
/// Patterns are stateless observations — they highlight correlations and
/// recurrences without storing mutable state or driving automated actions.
/// Every detected pattern includes an explicit count, rate, and description
/// so users can understand exactly why it was surfaced.
///
/// Confidence thresholds:
///   High   — pattern rate ≥ 60% across ≥ 5 observations
///   Medium — pattern rate ≥ 30% or ≥ 3 observations
///   Low    — pattern detected but infrequent or unconfirmed
///
/// Requires at least 2 sessions to detect any pattern.
/// </summary>
internal static class WorkloadPatternDetector
{
    // ── Thresholds ─────────────────────────────────────────────────────────────

    private const double HighRate   = 0.60;
    private const double MedRate    = 0.30;
    private const int    HighCount  = 5;
    private const int    MedCount   = 3;

    private const double HighCpuPeak = 80.0;
    private const double HighRamPeak = 80.0;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects recurring behavioral patterns from a collection of completed sessions.
    /// Returns an empty list when fewer than 2 sessions are available.
    /// </summary>
    public static IReadOnlyList<BehavioralPattern> DetectPatterns(
        IReadOnlyList<SessionSummary> sessions)
    {
        if (sessions.Count < 2) return [];

        var patterns = new List<BehavioralPattern>();

        DetectThermalStress(sessions, patterns);
        DetectMemoryPressure(sessions, patterns);
        DetectOvernightActivity(sessions, patterns);
        DetectStartupCongestion(sessions, patterns);
        DetectChronicHighLoad(sessions, patterns);
        DetectWorkloadTransitionFrequency(sessions, patterns);

        // Return highest-confidence patterns first
        return patterns
            .OrderByDescending(p => (int)p.Confidence)
            .ThenByDescending(p => p.ObservationCount)
            .ToList();
    }

    // ── Individual detectors ───────────────────────────────────────────────────

    private static void DetectThermalStress(
        IReadOnlyList<SessionSummary> sessions,
        List<BehavioralPattern> out_)
    {
        // Detect workload types that frequently coincide with thermal stress
        foreach (var group in sessions.GroupBy(s => s.DominantWorkload))
        {
            var list    = group.ToList();
            if (list.Count < 2) continue;

            int stressed = list.Count(s => s.HadThermalStress);
            if (stressed < 2) continue;

            double rate = (double)stressed / list.Count;
            if (rate < MedRate) continue;

            var conf    = Confidence(rate, stressed);
            var hot     = list.Where(s => s.HadThermalStress).ToList();
            double peak = hot.Max(s => s.PeakThermalC);

            out_.Add(new BehavioralPattern(
                PatternId:         $"thermal.{group.Key}",
                Label:             $"Thermal Stress During {MachineBehaviorModel.Label(group.Key)}",
                Description:       $"Thermal stress occurred in {stressed} of {list.Count} " +
                                   $"{MachineBehaviorModel.Label(group.Key)} sessions ({rate:P0}). " +
                                   $"Peak temperature reached {peak:F0}°C.",
                AssociatedWorkload: group.Key,
                ObservationCount:  stressed,
                FirstObservedAt:   hot.Min(s => s.StartedAt),
                LastObservedAt:    hot.Max(s => s.StartedAt),
                Confidence:        conf));
        }
    }

    private static void DetectMemoryPressure(
        IReadOnlyList<SessionSummary> sessions,
        List<BehavioralPattern> out_)
    {
        foreach (var group in sessions.GroupBy(s => s.DominantWorkload))
        {
            var list = group.ToList();
            if (list.Count < 2) continue;

            int pressured = list.Count(s => s.HadMemoryPressure);
            if (pressured < 2) continue;

            double rate = (double)pressured / list.Count;
            if (rate < MedRate) continue;

            var conf    = Confidence(rate, pressured);
            var pressed = list.Where(s => s.HadMemoryPressure).ToList();

            out_.Add(new BehavioralPattern(
                PatternId:         $"memory.{group.Key}",
                Label:             $"RAM Pressure During {MachineBehaviorModel.Label(group.Key)}",
                Description:       $"Memory pressure developed in {pressured} of {list.Count} " +
                                   $"{MachineBehaviorModel.Label(group.Key)} sessions ({rate:P0}). " +
                                   "This is a recurring pattern for this workload type.",
                AssociatedWorkload: group.Key,
                ObservationCount:  pressured,
                FirstObservedAt:   pressed.Min(s => s.StartedAt),
                LastObservedAt:    pressed.Max(s => s.StartedAt),
                Confidence:        conf));
        }
    }

    private static void DetectOvernightActivity(
        IReadOnlyList<SessionSummary> sessions,
        List<BehavioralPattern> out_)
    {
        var overnight = sessions
            .Where(s => s.DominantWorkload == WorkloadType.OvernightProcessing ||
                        IsOvernightHour(s.StartedAt))
            .ToList();

        if (overnight.Count < 2) return;

        var conf = Confidence((double)overnight.Count / sessions.Count, overnight.Count);

        out_.Add(new BehavioralPattern(
            PatternId:         "overnight.sessions",
            Label:             "Recurring Overnight Activity",
            Description:       $"This machine has been active during late-night hours in {overnight.Count} sessions. " +
                               "Background processing, scheduled tasks, or overnight downloads may be running.",
            AssociatedWorkload: WorkloadType.OvernightProcessing,
            ObservationCount:  overnight.Count,
            FirstObservedAt:   overnight.Min(s => s.StartedAt),
            LastObservedAt:    overnight.Max(s => s.StartedAt),
            Confidence:        conf));
    }

    private static void DetectStartupCongestion(
        IReadOnlyList<SessionSummary> sessions,
        List<BehavioralPattern> out_)
    {
        var startupHeavy = sessions
            .Where(s => s.DominantWorkload == WorkloadType.StartupHeavy)
            .ToList();

        if (startupHeavy.Count < 2) return;

        var conf = Confidence((double)startupHeavy.Count / sessions.Count, startupHeavy.Count);

        out_.Add(new BehavioralPattern(
            PatternId:         "startup.congestion",
            Label:             "Chronic Startup Congestion",
            Description:       $"Startup pressure has been observed in {startupHeavy.Count} sessions. " +
                               "A large number of startup apps may be delaying system readiness.",
            AssociatedWorkload: WorkloadType.StartupHeavy,
            ObservationCount:  startupHeavy.Count,
            FirstObservedAt:   startupHeavy.Min(s => s.StartedAt),
            LastObservedAt:    startupHeavy.Max(s => s.StartedAt),
            Confidence:        conf));
    }

    private static void DetectChronicHighLoad(
        IReadOnlyList<SessionSummary> sessions,
        List<BehavioralPattern> out_)
    {
        var highLoad = sessions
            .Where(s => s.PeakCpuPct >= HighCpuPeak || s.PeakRamPct >= HighRamPeak)
            .ToList();

        if (highLoad.Count < MedCount) return;

        double rate = (double)highLoad.Count / sessions.Count;
        var conf    = Confidence(rate, highLoad.Count);

        out_.Add(new BehavioralPattern(
            PatternId:         "load.high.chronic",
            Label:             "Recurring High Resource Pressure",
            Description:       $"High CPU or RAM peaks occurred in {highLoad.Count} of {sessions.Count} sessions " +
                               $"({rate:P0}). This machine regularly runs demanding workloads.",
            AssociatedWorkload: WorkloadType.Unknown,
            ObservationCount:  highLoad.Count,
            FirstObservedAt:   highLoad.Min(s => s.StartedAt),
            LastObservedAt:    highLoad.Max(s => s.StartedAt),
            Confidence:        conf));
    }

    private static void DetectWorkloadTransitionFrequency(
        IReadOnlyList<SessionSummary> sessions,
        List<BehavioralPattern> out_)
    {
        // Sessions with many workload type transitions indicate mixed-use patterns
        var mixed = sessions
            .Where(s => s.TransitionCount >= 3)
            .ToList();

        if (mixed.Count < 2) return;

        var conf = Confidence((double)mixed.Count / sessions.Count, mixed.Count);

        out_.Add(new BehavioralPattern(
            PatternId:         "workload.mixed.sessions",
            Label:             "Frequent Workload Switching",
            Description:       $"High workload variation was detected in {mixed.Count} sessions. " +
                               "This machine is used for diverse workload types in single sittings.",
            AssociatedWorkload: WorkloadType.Unknown,
            ObservationCount:  mixed.Count,
            FirstObservedAt:   mixed.Min(s => s.StartedAt),
            LastObservedAt:    mixed.Max(s => s.StartedAt),
            Confidence:        conf));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static BehaviorConfidence Confidence(double rate, int count) =>
        (rate >= HighRate && count >= HighCount) ? BehaviorConfidence.High :
        (rate >= MedRate  || count >= MedCount)  ? BehaviorConfidence.Medium :
        BehaviorConfidence.Low;

    private static bool IsOvernightHour(DateTimeOffset at)
    {
        int h = at.LocalDateTime.Hour;
        return h >= 22 || h <= 5;
    }
}
