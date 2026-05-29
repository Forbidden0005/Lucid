using System.Diagnostics;
using Lucid.Services.Diagnostics.Governance;
using Lucid.Services.Diagnostics.Reasoning;
using Lucid.Services.Infrastructure.Events;
using Lucid.Services.Infrastructure.OperationalState;
using Lucid.Services.Intelligence;
using Lucid.Services.Diagnostics.Logging;
using Lucid.Services.Reasoning.Context;
using Lucid.Services.Reasoning.Memory;
using Lucid.Services.Session;

namespace Lucid.Services.Reasoning.Cognitive;

/// <summary>
/// Production implementation of <see cref="ICognitiveReasoningEngine"/>.
///
/// Inference rule pipeline (evaluated in order per cycle):
///   1. StartupCongestionRule — post-boot resource pressure interpretation
///   2. SustainedPressureRule — prolonged elevated resource interpretation
///   3. ThermalContextRule    — thermal signals adjusted for workload context
///   4. StoragePressureRule   — disk pressure with trajectory interpretation
///   5. CombinedPressureRule  — multi-domain correlated pressure inference
///   6. SessionDegradationRule — performance worsening since session start
///
/// Each rule is deterministic and produces zero or one <see cref="OperationalInference"/>.
/// Rules share the <see cref="ReasoningContext"/> snapshot — no rule has side effects.
///
/// The engine dispatches a <see cref="CognitiveInferenceCycleCompletedEvent"/>
/// after each cycle so subscribers can react without polling.
/// </summary>
public sealed class CognitiveReasoningEngine : ICognitiveReasoningEngine
{
    private readonly ISystemInsightEngine            _insights;
    private readonly IOperationalStateCoordinator    _operationalState;
    private readonly IOperationalContextSynthesizer  _contextSynthesizer;
    private readonly IReasoningMemoryService         _memory;
    private readonly IEventBus                       _eventBus;
    private readonly IOperationalLogger              _logger;
    private readonly ISessionContextService          _session;

    // ── Phase 19 gap: governance & diagnostics integration ────────────────────
    private readonly GovernanceDiagnosticsService? _governanceDiagnostics;
    private readonly ReasoningDiagnosticsService?  _reasoningDiagnostics;
    private readonly CognitiveHealthMetrics        _healthMetrics;

    private volatile IReadOnlyList<OperationalInference> _current = [];
    private volatile ReasoningContext?                   _lastContext;
    // DateTime? cannot be volatile (value type) — use UTC ticks; -1 = never run.
    private long _lastAnalysisAtTicks = -1L;

    private const string Category = "CognitiveReasoning";

    /// <summary>
    /// Live health metrics for this reasoning engine.
    /// Exposed via <c>AppServices.CognitiveMetrics</c>.
    /// Thread-safe — Interlocked-backed counters.
    /// </summary>
    public CognitiveHealthMetrics HealthMetrics => _healthMetrics;

    public CognitiveReasoningEngine(
        ISystemInsightEngine           insights,
        IOperationalStateCoordinator   operationalState,
        IOperationalContextSynthesizer contextSynthesizer,
        IReasoningMemoryService        memory,
        IEventBus                      eventBus,
        IOperationalLogger             logger,
        ISessionContextService         session,
        // Optional Phase 19 gap additions — backward compatible (default null).
        GovernanceDiagnosticsService?  governanceDiagnostics = null,
        ReasoningDiagnosticsService?   reasoningDiagnostics  = null,
        CognitiveHealthMetrics?        healthMetrics         = null)
    {
        _insights              = insights;
        _operationalState      = operationalState;
        _contextSynthesizer    = contextSynthesizer;
        _memory                = memory;
        _eventBus              = eventBus;
        _logger                = logger;
        _session               = session;
        _governanceDiagnostics = governanceDiagnostics;
        _reasoningDiagnostics  = reasoningDiagnostics;
        _healthMetrics         = healthMetrics ?? new CognitiveHealthMetrics();
    }

    // ── ICognitiveReasoningEngine ─────────────────────────────────────────────

    public IReadOnlyList<OperationalInference> CurrentInferences => _current;
    public ReasoningContext?                   LastContext        => _lastContext;
    public DateTime? LastAnalysisAt =>
        _lastAnalysisAtTicks == -1L
            ? null
            : new DateTime(Interlocked.Read(ref _lastAnalysisAtTicks), DateTimeKind.Utc);

    public async Task<IReadOnlyList<OperationalInference>> AnalyzeAsync(
        CancellationToken cancellationToken = default)
    {
        var cycleStart  = DateTime.UtcNow;
        var stopwatch   = Stopwatch.StartNew();
        var correlation = CorrelationContext.New();
        _logger.Debug(Category, "Reasoning cycle starting", correlation);

        // Step 1: Synthesize context (< 5 ms).
        var context = await _contextSynthesizer.SynthesizeAsync(cancellationToken)
                           .ConfigureAwait(false);

        // Step 2: Build reasoning context snapshot.
        var state          = _operationalState.Current;
        var activeInsights = _insights.CurrentInsights;
        var sessionAge     = GetSessionAgeSeconds();

        var rc = BuildReasoningContext(state, activeInsights, context, sessionAge);
        _lastContext = rc;

        // Step 3: Evaluate inference rules — track fired vs skipped for diagnostics.
        var inferences   = new List<OperationalInference>();
        var firedRules   = new List<string>();
        var skippedRules = new List<string>();

        TrackRule("StartupCongestionRule", inferences,
            () => EvaluateStartupCongestionRule(rc, inferences), firedRules, skippedRules);
        TrackRule("SustainedPressureRule", inferences,
            () => EvaluateSustainedPressureRule(rc, inferences), firedRules, skippedRules);
        TrackRule("ThermalContextRule", inferences,
            () => EvaluateThermalContextRule(rc, context, inferences), firedRules, skippedRules);
        TrackRule("StoragePressureRule", inferences,
            () => EvaluateStoragePressureRule(rc, inferences), firedRules, skippedRules);
        TrackRule("CombinedPressureRule", inferences,
            () => EvaluateCombinedPressureRule(rc, inferences), firedRules, skippedRules);
        TrackRule("SessionDegradationRule", inferences,
            () => EvaluateSessionDegradationRule(rc, inferences), firedRules, skippedRules);

        // Step 4: Sort by priority descending.
        inferences.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        // Step 5: Record in memory (non-critical path — fire and forget).
        foreach (var inf in inferences)
        {
            try { _memory.Record(inf); }
            catch { /* memory recording is best-effort */ }
        }

        var result = (IReadOnlyList<OperationalInference>)inferences.AsReadOnly();
        _current = result;
        Interlocked.Exchange(ref _lastAnalysisAtTicks, DateTime.UtcNow.Ticks);

        stopwatch.Stop();

        // Step 6: Derive governance note from recent audit events (if wired).
        var governanceNote = BuildGovernanceNote();

        // Step 7: Update health metrics.
        var active     = inferences.Where(i => !i.IsSuppressed).ToList();
        var suppressed = inferences.Where(i => i.IsSuppressed).ToList();
        var maxConf    = active.Select(i => i.Confidence.Value).DefaultIfEmpty(0).Max();

        _healthMetrics.RecordCycle(
            inferenceCount:   inferences.Count,
            suppressedCount:  suppressed.Count,
            maxConfidence:    maxConf,
            durationMs:       stopwatch.Elapsed.TotalMilliseconds);

        // Step 8: Record a cognitive trace for the diagnostics service (if wired).
        if (_reasoningDiagnostics != null)
        {
            var trace = new CognitiveTraceRecord
            {
                CycleStartedAtUtc   = cycleStart,
                Duration            = stopwatch.Elapsed,
                Inferences          = result,
                SuppressedInferences = suppressed.AsReadOnly(),
                Context             = context,
                FiredRules          = firedRules.AsReadOnly(),
                SkippedRules        = skippedRules.AsReadOnly(),
                GovernanceNote      = governanceNote,
            };

            try { _reasoningDiagnostics.RecordCycle(trace); }
            catch { /* diagnostics recording is best-effort */ }
        }

        _logger.Debug(Category,
            $"Reasoning cycle complete — {inferences.Count} inference(s) in " +
            $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms", correlation);

        // Step 9: Publish cycle completion event.
        _ = _eventBus.PublishAsync(new CognitiveInferenceCycleCompletedEvent(
            InferenceCount: inferences.Count,
            HighestPriority: inferences.Count > 0
                ? inferences[0].Priority
                : InferencePriority.Background),
            EventDispatchMode.FireAndForget,
            CancellationToken.None);

        return result;
    }

    // ── Governance note ───────────────────────────────────────────────────────

    private string? BuildGovernanceNote()
    {
        if (_governanceDiagnostics == null) return null;

        var recent = _governanceDiagnostics.GetAuditLog().Take(10).ToList();
        if (recent.Count == 0) return null;

        // Tamper detection is the highest-priority governance signal.
        if (recent.Any(e => e.TransitionType == "TrustPostureTampered"))
            return "Trust posture tamper detected — safe defaults applied this session";

        // Elevated operation-denial rate signals a constrained trust environment.
        var denied = recent.Count(e => e.TransitionType == "OperationDenied");
        if (denied >= 3)
            return $"{denied} operations denied by governance policy in this session";

        // Endpoint rejections indicate an attempt to use a non-local LLM endpoint.
        if (recent.Any(e => e.TransitionType == "EndpointRejected"))
            return "Non-local LLM endpoint rejected — local-only policy enforced";

        return null;
    }

    // ── Context assembly ──────────────────────────────────────────────────────

    private static ReasoningContext BuildReasoningContext(
        OperationalStateSnapshot          state,
        IReadOnlyList<SystemInsight>      insights,
        ContextProfile                    context,
        double                            sessionAgeSeconds)
    {
        var bySeverity = insights
            .GroupBy(i => i.Severity.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return new ReasoningContext
        {
            CpuPercent            = state.CpuPercent,
            RamPercent            = state.RamPercent,
            ActiveInsights        = insights,
            InsightsBySeverity    = bySeverity,
            OperationalState      = state,
            CurrentContext        = context,
            SessionAgeSeconds     = sessionAgeSeconds,
        };
    }

    // ── Inference rules ───────────────────────────────────────────────────────

    private static void EvaluateStartupCongestionRule(
        ReasoningContext            rc,
        List<OperationalInference>  output)
    {
        if (!rc.IsPostBoot && !rc.IsEarlySession) return;
        if (rc.CpuPercent < 50 && rc.RamPercent < 70)  return;

        var startupInsights = rc.ActiveInsights
            .Where(i => i.Id.ToLowerInvariant().Contains("startup"))
            .ToList();

        if (startupInsights.Count == 0 && rc.CpuPercent < 65) return;

        var corroboration = startupInsights.Count;
        var score = ConfidenceScore.Compute(
            EvidenceStrength.Moderate,
            corroboration,
            [new UncertaintyContribution(
                UncertaintyFactor.ShortObservationWindow, 0.15,
                "System just started")]);

        var evidence = new List<ReasoningEvidence>
        {
            ReasoningEvidence.Telemetry(
                $"CPU at {rc.CpuPercent:F0}% during early boot window",
                EvidenceStrength.Moderate),
        };

        foreach (var s in startupInsights.Take(2))
            evidence.Add(ReasoningEvidence.Insight(
                s.Id ?? "", s.Title ?? "Startup insight", EvidenceStrength.Moderate));

        output.Add(new OperationalInference
        {
            Headline    = "Startup congestion is causing temporary resource pressure.",
            Explanation = $"CPU is at {rc.CpuPercent:F0}% and RAM at {rc.RamPercent:F0}% " +
                          "while startup services are initializing. This is typical and " +
                          "should resolve within a few minutes.",
            Trajectory  = "Resource usage will likely normalize as startup services complete.",
            Priority    = InferencePriority.Informational,
            Confidence  = score,
            Domains     = ["Startup", "Performance"],
            Evidence    = evidence.AsReadOnly(),
            WorkloadContextAtInference = rc.CurrentContext?.WorkloadLabel,
        });
    }

    private static void EvaluateSustainedPressureRule(
        ReasoningContext            rc,
        List<OperationalInference>  output)
    {
        if (rc.IsPostBoot || rc.IsEarlySession) return;
        if (!rc.HasSustainedHighCpu && !rc.HasRisingRamPressure) return;

        // Context check: is sustained pressure expected?
        bool contextExpected = rc.CurrentContext?.HighCpuIsExpected == true;

        var domain  = rc.HasSustainedHighCpu && rc.HasRisingRamPressure
            ? "Performance"
            : rc.HasSustainedHighCpu ? "Performance" : "Memory";

        var strength  = rc.CpuPercent > 90 || rc.RamPercent > 90
            ? EvidenceStrength.Strong
            : EvidenceStrength.Moderate;

        var uncertainties = new List<UncertaintyContribution>();
        if (contextExpected)
            uncertainties.Add(new UncertaintyContribution(
                UncertaintyFactor.AmbiguousWorkloadContext, 0.20,
                "Workload context may explain pressure"));

        var score = ConfidenceScore.Compute(EvidenceStrength.Strong,
            corroborationCount: rc.HasSustainedHighCpu && rc.HasRisingRamPressure ? 1 : 0,
            uncertainties);

        var evidence = new List<ReasoningEvidence>();
        if (rc.HasSustainedHighCpu)
            evidence.Add(ReasoningEvidence.Telemetry(
                $"CPU sustained at {rc.CpuPercent:F0}% (rising trend)", strength));
        if (rc.HasRisingRamPressure)
            evidence.Add(ReasoningEvidence.Telemetry(
                $"RAM at {rc.RamPercent:F0}% and rising", strength));

        var priority = contextExpected
            ? InferencePriority.Informational
            : (score.Level >= ConfidenceLevel.High
                ? InferencePriority.Elevated
                : InferencePriority.Advisory);

        output.Add(new OperationalInference
        {
            Headline    = contextExpected
                ? $"Resource pressure is elevated but expected for current workload."
                : $"Sustained {domain.ToLower()} pressure detected.",
            Explanation = contextExpected
                ? $"CPU is at {rc.CpuPercent:F0}% during {rc.CurrentContext?.WorkloadLabel}, " +
                  "which is within normal range for this workload type."
                : $"CPU has been elevated at {rc.CpuPercent:F0}% with a rising trend " +
                  "outside of expected workload patterns.",
            Trajectory  = rc.HasRisingRamPressure
                ? "RAM pressure may continue to increase if no action is taken."
                : null,
            Priority    = priority,
            Confidence  = score,
            Domains     = [domain],
            Evidence    = evidence.AsReadOnly(),
            WorkloadContextAtInference = rc.CurrentContext?.WorkloadLabel,
            IsSuppressed   = contextExpected,
            SuppressionReason = contextExpected
                ? $"Expected during {rc.CurrentContext?.WorkloadLabel}"
                : null,
        });
    }

    private static void EvaluateThermalContextRule(
        ReasoningContext            rc,
        ContextProfile?             context,
        List<OperationalInference>  output)
    {
        if (!rc.HasThermalStress) return;

        bool thermalExpected = context?.ThermalElevationIsExpected == true;
        var expected         = context?.ExpectedThermalRange ?? (30.0, 100.0);
        bool withinRange     = rc.CpuTemperatureCelsius <= expected.High;

        if (thermalExpected && withinRange) return; // suppress expected thermal during heavy workload

        var score = ConfidenceScore.Compute(
            rc.CpuTemperatureCelsius > 90 ? EvidenceStrength.Strong : EvidenceStrength.Moderate);

        output.Add(new OperationalInference
        {
            Headline    = "CPU thermal readings are elevated.",
            Explanation = $"CPU temperature is at {rc.CpuTemperatureCelsius:F0}°C, " +
                          "which is above the comfortable operating range. " +
                          (thermalExpected
                              ? $"Some elevation is expected during {context?.WorkloadLabel}."
                              : "This is higher than expected for the current workload."),
            Trajectory  = rc.CpuTemperatureCelsius > 90
                ? "Continued thermal elevation may trigger thermal throttling, reducing performance."
                : null,
            Priority    = rc.CpuTemperatureCelsius > 90
                ? InferencePriority.Elevated
                : InferencePriority.Advisory,
            Confidence  = score,
            Domains     = ["Thermal"],
            Evidence    =
            [
                ReasoningEvidence.Telemetry(
                    $"CPU temperature at {rc.CpuTemperatureCelsius:F0}°C",
                    EvidenceStrength.Strong)
            ],
            WorkloadContextAtInference = context?.WorkloadLabel,
        });
    }

    private static void EvaluateStoragePressureRule(
        ReasoningContext            rc,
        List<OperationalInference>  output)
    {
        if (!rc.OperationalState.HasCondition(
            Lucid.Services.Infrastructure.OperationalState.RuntimeCondition.LowDiskSpace))
            return;

        var storageInsights = rc.ActiveInsights
            .Where(i => i.Id.ToLowerInvariant().Contains("storage") ||
                        i.Id.ToLowerInvariant().Contains("disk"))
            .ToList();

        if (storageInsights.Count == 0) return;

        var score = ConfidenceScore.Compute(EvidenceStrength.Strong, storageInsights.Count - 1);

        output.Add(new OperationalInference
        {
            Headline    = "Storage space is critically low.",
            Explanation = "Available disk space has fallen below safe operating levels. " +
                          "Low storage can impact system performance, prevent updates, " +
                          "and cause application failures.",
            Trajectory  = "System stability may degrade further as disk space approaches zero.",
            Priority    = InferencePriority.Elevated,
            Confidence  = score,
            Domains     = ["Storage"],
            Evidence    = [.. storageInsights.Take(2).Select(i =>
                ReasoningEvidence.Insight(i.Id ?? "", i.Title ?? "Storage insight",
                    EvidenceStrength.Strong))],
            WorkloadContextAtInference = rc.CurrentContext?.WorkloadLabel,
        });
    }

    private static void EvaluateCombinedPressureRule(
        ReasoningContext            rc,
        List<OperationalInference>  output)
    {
        int pressureDomains = 0;
        if (rc.HasSustainedHighCpu)     pressureDomains++;
        if (rc.HasRisingRamPressure)    pressureDomains++;
        if (rc.HasThermalStress)        pressureDomains++;

        if (pressureDomains < 2) return;
        if (rc.IsPostBoot || rc.CurrentContext?.HighCpuIsExpected == true) return;

        var score = ConfidenceScore.Compute(
            EvidenceStrength.Strong,
            pressureDomains - 1); // each extra domain corroborates

        output.Add(new OperationalInference
        {
            Headline    = $"Multi-domain resource pressure across {pressureDomains} areas.",
            Explanation = $"CPU, RAM, and thermal signals are all elevated simultaneously " +
                          "outside of expected workload context. This pattern suggests the " +
                          "system is under significant combined stress.",
            Trajectory  = "Combined pressure increases risk of performance degradation " +
                          "and may affect system responsiveness.",
            Priority    = InferencePriority.Elevated,
            Confidence  = score,
            Domains     = ["Performance", "Thermal"],
            Evidence    =
            [
                ReasoningEvidence.Telemetry(
                    $"CPU {rc.CpuPercent:F0}%, RAM {rc.RamPercent:F0}%, " +
                    $"Thermal {rc.CpuTemperatureCelsius:F0}°C — simultaneous elevation",
                    EvidenceStrength.Strong)
            ],
            WorkloadContextAtInference = rc.CurrentContext?.WorkloadLabel,
        });
    }

    private static void EvaluateSessionDegradationRule(
        ReasoningContext            rc,
        List<OperationalInference>  output)
    {
        // Only evaluate for sessions > 30 minutes old.
        if (rc.SessionAgeSeconds < 1800) return;

        // Look for a pattern of worsening trends.
        bool cpuWorsening  = rc.CpuTrend5Min > 5.0 && rc.CpuPercent > 60;
        bool ramWorsening  = rc.RamTrend5Min > 3.0 && rc.RamPercent > 75;

        if (!cpuWorsening && !ramWorsening) return;

        var score = ConfidenceScore.Compute(
            EvidenceStrength.Moderate,
            cpuWorsening && ramWorsening ? 1 : 0,
            [new UncertaintyContribution(
                UncertaintyFactor.AmbiguousWorkloadContext, 0.10)]);

        output.Add(new OperationalInference
        {
            Headline    = "System performance has been trending downward during this session.",
            Explanation = "Resource usage has been gradually increasing over the last " +
                          $"{rc.SessionAgeSeconds / 60:F0} minutes without a corresponding " +
                          "change in workload. This may indicate a memory leak, background " +
                          "service accumulation, or gradual performance degradation.",
            Trajectory  = "If the trend continues, performance impact may become noticeable.",
            Priority    = InferencePriority.Advisory,
            Confidence  = score,
            Domains     = ["Performance"],
            Evidence    =
            [
                ReasoningEvidence.Telemetry(
                    $"CPU trend +{rc.CpuTrend5Min:F1}%, RAM trend +{rc.RamTrend5Min:F1}% over 5 min",
                    EvidenceStrength.Moderate)
            ],
            WorkloadContextAtInference = rc.CurrentContext?.WorkloadLabel,
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private double GetSessionAgeSeconds()
    {
        try { return _session.Current.SessionAge.TotalSeconds; }
        catch { return 0; }
    }

    /// <summary>
    /// Invokes a rule evaluation action and records the rule name in either
    /// <paramref name="firedRules"/> or <paramref name="skippedRules"/> depending
    /// on whether the rule produced a new inference.
    /// </summary>
    private static void TrackRule(
        string                     ruleName,
        List<OperationalInference> inferences,
        Action                     evaluate,
        List<string>               firedRules,
        List<string>               skippedRules)
    {
        var countBefore = inferences.Count;
        evaluate();
        if (inferences.Count > countBefore)
            firedRules.Add(ruleName);
        else
            skippedRules.Add(ruleName);
    }
}
