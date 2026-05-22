using ExplainMyPC.Services;
using ExplainMyPC.Services.Analytics;
using ExplainMyPC.Services.Baseline;
using ExplainMyPC.Services.Behavior;
using ExplainMyPC.Services.Diagnostics;
using ExplainMyPC.Services.Distributed;
using ExplainMyPC.Services.Governance;
using ExplainMyPC.Services.Learning;
using ExplainMyPC.Services.Execution;
using ExplainMyPC.Services.Execution.Executors;
using ExplainMyPC.Services.Explain;
using ExplainMyPC.Services.History;
using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Narrative;
using ExplainMyPC.Services.Persistence;
using ExplainMyPC.Services.Replay;
using ExplainMyPC.Services.Security;
using ExplainMyPC.Services.Session;
using ExplainMyPC.Services.Startup;
using ExplainMyPC.Services.Storage;
using ExplainMyPC.Services.Timeline;
using ExplainMyPC.Services.Remediation;
using ExplainMyPC.Services.Simulation;
using ExplainMyPC.Services.Watchtower;
using Microsoft.UI.Dispatching;


namespace ExplainMyPC;

/// <summary>
/// Lightweight application-level service registry.
///
/// Provides a straightforward alternative to a full DI container at
/// ExplainMyPC's current scale. All services are created once from
/// Initialize(), started, and live for the application lifetime.
///
/// To add a new service:
///   1. Define an interface in Services/.
///   2. Add a typed property here.
///   3. Wire up the concrete class in Initialize().
///
/// Usage:
///   Call Initialize() from App.OnLaunched before the main window opens.
///   Call Shutdown() from the main window's Closed handler.
/// </summary>
public static class AppServices
{
    private static ITelemetryService?           _telemetry;
    private static ITelemetryHistoryBuffer?     _history;
    private static ISystemBaselineService?      _baseline;
    private static ISystemInsightEngine?        _intelligence;
    private static ISessionContextService?      _session;
    private static IOperationalNarrativeEngine? _narrative;
    private static ActionExecutorRegistry?      _executorRegistry;
    private static IActionExecutionEngine?      _executionEngine;
    private static IOperationHistoryService?    _operationHistory;
    private static ITimelineAggregationService? _timeline;
    private static IStartupManagementService?   _startupManagement;
    private static IExplainMyPcEngine?          _explainEngine;
    private static IOperationalReplayService?    _replayService;
    private static IRemediationLearningService?  _learningService;

    // ── SQLite persistence layer ──────────────────────────────────────────────
    private static SQLitePersistenceService?        _persistence;
    private static HistoricalTelemetryRepository?   _telHistoryRepo;
    private static TimelineEventRepository?         _timelineEventRepo;
    private static InsightHistoryRepository?        _insightHistoryRepo;
    private static RecommendationOutcomeRepository? _outcomeRepo;
    private static IHistoricalAnalyticsEngine?      _historicalAnalytics;
    private static HashSet<string>                  _lastInsightIds = [];
    private static System.Threading.Timer?          _downsampleTimer;

    // ── Runtime governance layer ──────────────────────────────────────────────
    private static ConcurrencyBudget?          _concurrencyBudget;
    private static ExecutionPriorityQueue?     _executionQueue;
    private static PollingCoordinator?         _pollingCoordinator;
    private static IRuntimeGovernanceService?  _governance;

    // ── Internal diagnostics layer ────────────────────────────────────────────
    private static InternalDiagnosticsService? _diagnostics;

    // ── Behavioral context layer ──────────────────────────────────────────────
    private static WorkloadProfilingService?  _workloadProfiling;
    private static IBehavioralContextEngine?  _behavioralContext;

    // ── Distributed intelligence layer ────────────────────────────────────────
    private static DeviceIdentityService?          _deviceIdentity;
    private static TrustedDeviceRegistry?          _trustedDevices;
    private static LocalSyncCoordinator?           _localSync;
    private static DistributedTimelineAggregator?  _distributedTimeline;
    private static CrossMachineAnalyticsEngine?    _crossMachineAnalytics;

    // ── Operational Watchtower layer ──────────────────────────────────────────
    private static ProactiveRecommendationCoordinator? _watchtowerCoordinator;
    private static OperationalWatchtowerService?       _watchtower;

    // ── Anomaly Intelligence layer ────────────────────────────────────────────
    private static IBehavioralBaselineService?  _behavioralBaseline;
    private static IDriftDetectionService?      _driftDetection;
    private static IAnomalyDetectionService?    _anomalyDetection;
    private static IEarlyWarningService?        _earlyWarning;
    private static ISystemPersonalityClassifier? _personalityClassifier;

    // ── Autonomous Remediation layer ──────────────────────────────────────────
    private static AutonomousRemediationService? _remediationService;

    // ── Operational Simulation layer ──────────────────────────────────────────
    private static OperationalSimulationEngine?  _simulationEngine;
    private static ISimulationHistoryService?    _simulationHistory;
    private static IOutcomeVerificationService?  _outcomeVerification;

    // ── Machine health trajectory layer ───────────────────────────────────────
    private static IMachineHealthTrajectoryService? _healthTrajectory;

    // ── Adaptive Personalization layer ────────────────────────────────────────
    private static IInterventionMemoryService?          _interventionMemory;
    private static IPersonalizationEngine?              _personalizationEngine;
    private static IUserBehaviorClassifier?             _userBehaviorClassifier;
    private static IAlertFatigueManager?                _alertFatigueManager;
    private static IRecommendationExplanationService?   _recommendationExplanation;

    // ── Service accessors ─────────────────────────────────────────────────────

    /// <summary>Live hardware telemetry — CPU, RAM, GPU, Disk, Thermal.</summary>
    public static ITelemetryService Telemetry =>
        _telemetry ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Rolling 30-minute telemetry history buffer.
    /// Feeds trend analysis, anomaly detection, and the Explain My PC engine.
    /// </summary>
    public static ITelemetryHistoryBuffer History =>
        _history ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Adaptive machine-specific behavioral baseline service.
    /// Observes telemetry over time and learns this machine's normal operating
    /// ranges for CPU, RAM, temperature, and disk I/O.
    /// Baseline-aware insight rules use this to detect machine-specific anomalies.
    /// </summary>
    public static ISystemBaselineService Baseline =>
        _baseline ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Rule-based intelligence engine that evaluates heuristics on every
    /// telemetry tick and publishes findings when the active set changes.
    /// </summary>
    public static ISystemInsightEngine Intelligence =>
        _intelligence ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Session context service that tracks system uptime, sleep/wake cycles,
    /// post-login windows, idle periods, and insight onset times.
    /// Provides temporal anchoring for the narrative engine.
    /// </summary>
    public static ISessionContextService Session =>
        _session ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Deterministic narrative engine that synthesizes active intelligence findings
    /// into multi-paragraph, human-readable system summaries.
    /// Subscribes to InsightsUpdated and re-generates prose on each change.
    /// </summary>
    public static IOperationalNarrativeEngine Narrative =>
        _narrative ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Registry of all registered action executors.
    /// Use to check whether a specific action is implemented, or to
    /// register new executors during startup.
    /// </summary>
    public static ActionExecutorRegistry ExecutorRegistry =>
        _executorRegistry ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Action execution engine. The single entry point for running or
    /// rolling back recommended remediation actions.
    /// Phase-1 executors (open Task Manager, Storage Sense, Startup Apps,
    /// Windows Security) are registered at startup. Actions without a
    /// registered executor return
    /// <see cref="ActionExecutionStatus.ExecutorNotFound"/>.
    /// </summary>
    public static IActionExecutionEngine ExecutionEngine =>
        _executionEngine ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Persistent operational history service. Records every execution and
    /// rollback so users and developers can audit what the app has done.
    /// All writes are best-effort; a failure never disrupts execution.
    /// </summary>
    public static IOperationHistoryService HistoryService =>
        _operationHistory ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Unified event stream aggregating intelligence findings, session events,
    /// narrative checkpoints, and action history into a single chronological log.
    /// Powers the Operational Timeline page.
    /// </summary>
    public static ITimelineAggregationService Timeline =>
        _timeline ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Read/write access to Windows startup entries.
    /// Enables and disables startup items via the StartupApproved registry
    /// mechanism (the same mechanism Windows Task Manager uses).
    /// </summary>
    public static IStartupManagementService StartupManagement =>
        _startupManagement ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// ExplainMyPC operational reasoning engine — the flagship feature.
    /// Synthesizes insights, timeline events, narrative, and baseline data into
    /// a single coherent OperationalExplanation that drives the Explain My PC page.
    /// </summary>
    public static IExplainMyPcEngine ExplainEngine =>
        _explainEngine ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Operational replay and causal investigation engine.
    /// Reconstructs historical system state at any point in time from the
    /// rolling telemetry buffer, timeline event stream, and operation history.
    /// </summary>
    public static IOperationalReplayService ReplayService =>
        _replayService ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Adaptive remediation learning service.
    /// Observes before/after system state around each action execution and
    /// builds per-action effectiveness profiles over time.
    /// All analysis is deterministic, local-only, and cold-start safe.
    /// </summary>
    public static IRemediationLearningService LearningService =>
        _learningService ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Historical analytics engine.
    /// Computes long-term health scores, metric trends, recurring patterns,
    /// and narrative summaries from the SQLite operational database.
    /// All computation is local-only and deterministic — no cloud, no ML.
    /// </summary>
    public static IHistoricalAnalyticsEngine HistoricalAnalytics =>
        _historicalAnalytics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Runtime governance service.
    /// Monitors system pressure (CPU, GPU, battery, thermal) and manages
    /// concurrency budgets, adaptive polling rates, and workload deferral.
    /// All services that run heavy background work consult this service
    /// before starting to ensure ExplainMyPC stays lightweight.
    /// </summary>
    public static IRuntimeGovernanceService Governance =>
        _governance ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Internal diagnostics service.
    /// Tracks service health, sampler failures, executor crash history,
    /// runtime anomalies, and exposes actionable recovery operations.
    /// All methods are fire-and-forget and never throw.
    /// </summary>
    public static IInternalDiagnosticsService Diagnostics =>
        _diagnostics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Workload profiling service.
    /// Observes telemetry to classify the current workload type, track
    /// session peaks, and accumulate a behavioral history for this machine.
    /// </summary>
    public static WorkloadProfilingService WorkloadProfiling =>
        _workloadProfiling ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Behavioral context engine.
    /// Provides unified workload context, machine identity, detected patterns,
    /// and workload-aware recommendation deferral logic.
    /// </summary>
    public static IBehavioralContextEngine BehavioralContext =>
        _behavioralContext ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Device identity service.
    /// Generates and caches a stable device ID and hardware role classification.
    /// </summary>
    public static DeviceIdentityService DeviceIdentity =>
        _deviceIdentity ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Trusted device registry.
    /// Persists paired device records and shared AES encryption keys.
    /// </summary>
    public static TrustedDeviceRegistry TrustedDevices =>
        _trustedDevices ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Local sync coordinator.
    /// Manages LAN-only discovery (UDP) and encrypted snapshot exchange (TCP).
    /// </summary>
    public static LocalSyncCoordinator LocalSync =>
        _localSync ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Distributed timeline aggregator.
    /// Merges local timeline events with remote device snapshots into a unified stream.
    /// </summary>
    public static DistributedTimelineAggregator DistributedTimeline =>
        _distributedTimeline ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Cross-machine analytics engine.
    /// Compares snapshots across linked devices to surface ecosystem insights.
    /// </summary>
    public static CrossMachineAnalyticsEngine CrossMachineAnalytics =>
        _crossMachineAnalytics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Operational Watchtower service.
    /// Runs autonomous 30-minute analysis cycles detecting degradation, drift,
    /// and stability risks from historical operational data.
    /// All analysis is local-only, deterministic, and suggestion-only.
    /// </summary>
    public static OperationalWatchtowerService Watchtower =>
        _watchtower ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Autonomous remediation orchestration service.
    /// Plans, executes, and validates multi-step remediation workflows with
    /// full rollback support and user-controlled trust levels.
    /// Workflows NEVER execute without explicit user approval.
    /// All execution is local-only, logged, and reversible where possible.
    /// </summary>
    public static AutonomousRemediationService RemediationService =>
        _remediationService ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Operational simulation engine — "What If?" scenario modeling.
    /// Projects the likely outcome of interventions before the user commits to them.
    /// Deterministic, local-only, and stateless. No cloud, no ML, no side effects.
    /// Confidence is bounded at 88% to acknowledge inherent real-world uncertainty.
    /// </summary>
    public static OperationalSimulationEngine SimulationEngine =>
        _simulationEngine ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Simulation history service.
    /// Persists the last 20 simulation snapshots to
    /// %LOCALAPPDATA%\ExplainMyPC\simulation_history.json.
    /// All writes are best-effort — a write failure is never surfaced.
    /// </summary>
    public static ISimulationHistoryService SimulationHistory =>
        _simulationHistory ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Outcome verification service.
    /// Tracks predicted vs actual outcomes for completed simulations and
    /// computes prediction accuracy scores.  Pending verifications are
    /// in-memory only; evaluated outcomes persist to
    /// %LOCALAPPDATA%\ExplainMyPC\simulation_outcomes.json (max 50).
    /// </summary>
    public static IOutcomeVerificationService OutcomeVerification =>
        _outcomeVerification ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Machine health trajectory service.
    /// Wraps <see cref="HistoricalAnalytics"/> to produce a concise
    /// <see cref="MachineHealthReport"/> driving the Dashboard health card.
    /// Caches the last report so navigating back to the Dashboard is instant.
    /// </summary>
    public static IMachineHealthTrajectoryService HealthTrajectory =>
        _healthTrajectory ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Behavioral baseline adapter.
    /// Exposes a clean <see cref="BehavioralBaseline"/> snapshot from the live Welford model.
    /// Stateless — no Start()/Stop() needed.
    /// </summary>
    public static IBehavioralBaselineService BehavioralBaseline =>
        _behavioralBaseline ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Drift detection service.
    /// Adapts Watchtower drift observations into <see cref="DetectedDrift"/> records.
    /// Updates at Watchtower cadence (~30-minute cycles).
    /// </summary>
    public static IDriftDetectionService DriftDetection =>
        _driftDetection ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Anomaly detection service.
    /// Detects short-term behavioral anomalies via z-score comparison to the machine baseline.
    /// Updates at telemetry cadence (~1.5 s) when the anomaly set changes.
    /// </summary>
    public static IAnomalyDetectionService AnomalyDetection =>
        _anomalyDetection ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Early warning service.
    /// Synthesizes Watchtower alerts and anomaly detections into proactive warnings.
    /// </summary>
    public static IEarlyWarningService EarlyWarning =>
        _earlyWarning ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// System personality classifier.
    /// Classifies this machine's operational personality from historical and live signals.
    /// Stateless — call Classify() directly.
    /// </summary>
    public static ISystemPersonalityClassifier PersonalityClassifier =>
        _personalityClassifier ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Intervention memory service.
    /// Records every accept/dismiss interaction with a recommended action.
    /// Persists to %LOCALAPPDATA%\ExplainMyPC\intervention_memory.json.
    /// Local-only — no cloud, no telemetry upload.
    /// </summary>
    public static IInterventionMemoryService InterventionMemory =>
        _interventionMemory ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Personalization engine.
    /// Builds a PersonalizationProfile from InterventionMemory records.
    /// Pure function — stateless, no side effects.
    /// </summary>
    public static IPersonalizationEngine PersonalizationEngine =>
        _personalizationEngine ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// User behavior classifier.
    /// Converts a PersonalizationProfile into a human-readable OperationalStyleReport.
    /// Pure function — stateless, no side effects.
    /// </summary>
    public static IUserBehaviorClassifier UserBehaviorClassifier =>
        _userBehaviorClassifier ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Alert fatigue manager.
    /// Computes a suppression factor for actions shown repeatedly without acceptance.
    /// Transparent — the UI always shows when and why suppression is active.
    /// </summary>
    public static IAlertFatigueManager AlertFatigueManager =>
        _alertFatigueManager ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Recommendation explanation service.
    /// Generates human-readable "why recommended" text from ScoreBreakdown and profile.
    /// </summary>
    public static IRecommendationExplanationService RecommendationExplanation =>
        _recommendationExplanation ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and starts all application services.
    /// Must be called on the UI thread so services can capture the
    /// DispatcherQueue used to marshal readings back to the UI.
    /// </summary>
    public static void Initialize(DispatcherQueue uiDispatcher)
    {
        // ── SQLite persistence layer ──────────────────────────────────────────
        // Initialised first so repositories are ready before any services start
        // and before telemetry events begin firing.
        // InitializeAsync() is synchronous at app start — SQLite schema creation
        // is fast (< 50 ms) and the window has not been shown yet.
        var dbService = new SQLitePersistenceService();
        dbService.InitializeAsync().GetAwaiter().GetResult();
        _telHistoryRepo    = new HistoricalTelemetryRepository(dbService);
        _timelineEventRepo = new TimelineEventRepository(dbService);
        _insightHistoryRepo = new InsightHistoryRepository(dbService);
        _outcomeRepo       = new RecommendationOutcomeRepository(dbService);
        _historicalAnalytics = new HistoricalAnalyticsEngine(
            _telHistoryRepo, _insightHistoryRepo, _timelineEventRepo);
        _persistence = dbService;

        // ── Runtime governance layer ──────────────────────────────────────────
        // Created before telemetry so we can register the telemetry service as
        // an adaptive target immediately after it is constructed.
        _concurrencyBudget  = new ConcurrencyBudget(initialMaxBackground: 3);
        _executionQueue     = new ExecutionPriorityQueue();
        _pollingCoordinator = new PollingCoordinator();

        _telemetry = new WindowsTelemetryService(uiDispatcher);
        _history   = new TelemetryHistoryBuffer();

        // Register the telemetry service as the adaptive target so the governance
        // layer can adjust polling intervals when the runtime mode changes.
        _pollingCoordinator.RegisterTarget((WindowsTelemetryService)_telemetry);

        // Adaptive baseline service — learns this machine's normal operating
        // ranges over time and persists them to %LOCALAPPDATA%\ExplainMyPC\.
        // Must be created before SystemInsightEngine so the baseline rules
        // receive a valid service reference at construction time.
        _baseline = new SystemBaselineService(_telemetry);

        _intelligence = new SystemInsightEngine(_telemetry, _history, _baseline);

        // Track insight onset and resolution in the SQLite insight_history table.
        // Diffing previous vs. new set: new IDs = onset, missing IDs = resolved.
        // Fire-and-forget async writes — failures are swallowed by repository.
        _intelligence.InsightsUpdated += (_, insights) =>
        {
            var newIds = new HashSet<string>(
                insights.Select(i => i.Id), StringComparer.Ordinal);

            foreach (var insight in insights)
            {
                if (!_lastInsightIds.Contains(insight.Id))
                    _ = _insightHistoryRepo!.RecordOnsetAsync(insight);
            }
            foreach (var oldId in _lastInsightIds)
            {
                if (!newIds.Contains(oldId))
                    _ = _insightHistoryRepo!.RecordResolutionAsync(oldId);
            }
            _lastInsightIds = newIds;
        };

        // Every snapshot flows into the history buffer automatically.
        // ReadingAvailable fires on the UI thread, so Record() is always
        // called from a single thread — the write lock is still held for
        // correctness when background analysis threads read concurrently.
        _telemetry.ReadingAvailable += (_, snapshot) => _history.Record(snapshot);

        // Persist one sample every 30 seconds to the SQLite telemetry table.
        // EnqueueSample() is throttled internally; extra calls are no-ops.
        _telemetry.ReadingAvailable += (_, snapshot) => _telHistoryRepo!.EnqueueSample(
            snapshot.CpuPercent,
            snapshot.RamPercent,
            snapshot.GpuAvailable ? snapshot.GpuPercent : null,
            snapshot.DiskPercent,
            snapshot.CpuTemperatureAvailable ? snapshot.CpuTemperatureCelsius : null);

        // Session context service — tracks boot time, sleep/wake cycles, idle periods,
        // and insight onset times. Created after _intelligence so the InsightsUpdated
        // subscription is valid at construction time. Started after _intelligence.Start()
        // so the engine is already running when the first InsightsUpdated fires.
        _session = new SessionContextService(_telemetry, _intelligence);

        // Narrative engine synthesizes active findings into human-readable prose.
        // Receives the session context so it can add temporal and phase context to prose.
        // Created after both the intelligence engine and session service.
        _narrative = new OperationalNarrativeEngine(_intelligence, _telemetry, _baseline, _session);

        _telemetry.Start();
        _baseline.Start();      // must start after _telemetry so ReadingAvailable exists
        _intelligence.Start();
        _session.Start();       // must start after _intelligence (subscribes to InsightsUpdated)
        _narrative.Start();     // must start after _intelligence

        // ── Behavioral context layer ──────────────────────────────────────────
        // Started after session so the classifier receives full session context.
        // WorkloadProfilingService subscribes to ReadingAvailable internally.
        _workloadProfiling = new WorkloadProfilingService(_telemetry, _session);
        _workloadProfiling.Start();
        _behavioralContext = new BehavioralContextEngine(_workloadProfiling);

        // ── Runtime governance service ────────────────────────────────────────
        // Started after telemetry so ReadingAvailable is already firing.
        // The governance service subscribes internally via its own Start() call.
        _governance = new RuntimeGovernanceService(
            _telemetry, _pollingCoordinator!, _concurrencyBudget!, _executionQueue!, uiDispatcher);
        _governance.Start();

        // ── Internal diagnostics service ──────────────────────────────────────
        // Created after governance so all platform components exist.
        // Uses concrete type internally so we can call diagnostics-internal methods
        // (OnTelemetryReceived, OnGovernanceModeChanged) not on the public interface.
        _diagnostics = new InternalDiagnosticsService(
            uiDispatcher,
            _persistence,
            _concurrencyBudget,
            _pollingCoordinator,
            _executionQueue);

        // Wire telemetry heartbeat + overrun detection.
        _telemetry.ReadingAvailable += (_, snapshot) =>
            _diagnostics.OnTelemetryReceived(
                DateTimeOffset.Now,
                _pollingCoordinator!.CurrentTelemetryInterval);

        // Wire governance mode-change notifications to diagnostics.
        _governance.ModeChanged += (_, args) =>
            _diagnostics.OnGovernanceModeChanged(args.PreviousMode, args.NewMode, args.Reasons);

        _diagnostics.Start();

        // ── Operational history ───────────────────────────────────────────────
        // Initialised before the execution engine so it is ready the moment
        // the first action completes. JSON file created lazily on first write.
        _operationHistory = new OperationHistoryService();

        // ── Startup management service ────────────────────────────────────────
        // Write-side complement to StartupSampler. Uses the Windows
        // StartupApproved registry mechanism to enable/disable entries.
        // Created before the executor registry so executors can share the instance.
        _startupManagement = new StartupManagementService();

        // ── Remediation execution engine ──────────────────────────────────────
        _executorRegistry = new ActionExecutorRegistry();
        _executorRegistry.RegisterAll([
            // ── Guided navigation (Phase 1) ───────────────────────────────────
            // Open the relevant system tool — completely safe, no modification.
            new OpenTaskManagerExecutor(),      // action.cpu.open-task-manager
            new OpenStorageSenseExecutor(),     // action.disk.run-storage-sense
            new OpenStartupAppsExecutor(),      // action.startup.open-startup-apps
            new OpenWindowsSecurityExecutor(),  // action.security.open-windows-security

            // ── Disk cleanup (Phase 2) ────────────────────────────────────────
            // Staging-based safe cleanup with dry-run preview and rollback.
            new TempFileCleanupExecutor(),      // action.disk.clean-temp-files

            // ── Disk cleanup (Phase 3 — operational tools) ────────────────────
            new RecycleBinCleanupExecutor(),              // action.disk.empty-recycle-bin
            new WindowsUpdateCacheExecutor(),             // action.disk.clean-windows-update-cache
            new DeliveryOptimizationCacheExecutor(),      // action.disk.clean-delivery-optimization
            new BrowserCacheCleanupExecutor(),            // action.disk.clean-browser-cache

            // ── Startup management (Phase 3) ──────────────────────────────────
            new StartupAppDisableExecutor(_startupManagement),      // action.startup.disable-startup-app
            new StartupAppEnableExecutor(_startupManagement),       // action.startup.enable-startup-app
            new StartupStateBackupExecutor(_startupManagement),     // action.startup.backup-startup-state
            new StartupStateRestoreExecutor(_startupManagement),    // action.startup.restore-startup-state

            // ── Repair & recovery (Phase 4) ───────────────────────────────────
            // Safe wrappers around trusted Windows repair tools. All stream
            // live output, support dry-run explanation mode, and are
            // elevation-aware.  None support rollback (system repairs are
            // one-way by nature).
            new SfcScanExecutor(),               // action.repair.sfc-scannow
            new DismRestoreHealthExecutor(),      // action.repair.dism-restore-health
            new FlushDnsExecutor(),               // action.network.flush-dns
            new WinsockResetExecutor(),           // action.network.winsock-reset
            new WindowsStoreResetExecutor(),      // action.apps.reset-windows-store
            new NetworkAdapterResetExecutor(),    // action.network.reset-adapter

            // ── Storage intelligence (Phase 5) ────────────────────────────────
            new DeleteLargeFileExecutor(),        // action.storage.delete-large-file
            new DeleteDuplicateGroupExecutor(),   // action.storage.delete-duplicate-group
            new CleanOldDownloadsExecutor(),      // action.storage.clean-old-downloads

            // ── Process intelligence (Phase 6) ────────────────────────────────
            new TerminateProcessExecutor(),       // action.process.terminate
            new OpenProcessLocationExecutor(),    // action.process.open-location

            // ── Security intelligence (Phase 7) ───────────────────────────────
            new OpenVirusTotalExecutor(),         // action.security.open-virustotal
        ]);
        // Wrap in GovernanceAwareExecutionEngine so heavy actions check the
        // concurrency budget before dispatching. Lightweight navigation-only
        // actions are passed through without governance overhead.
        // The diagnostics callback forwards execution results so the diagnostics
        // layer can track executor health without a direct dependency.
        var rawEngine = new ActionExecutionEngine(_executorRegistry);
        _executionEngine = new GovernanceAwareExecutionEngine(
            rawEngine,
            _governance!,
            onExecutionResult: (actionId, success, errorDetail) =>
                _diagnostics?.RecordExecutorResult(actionId, success, errorDetail));

        // ── Operational Timeline ──────────────────────────────────────────────
        // Aggregates events from intelligence, session, narrative, and history.
        // Created after all of its upstream services are running.
        // Passes the DispatcherQueue so history load results can be marshalled
        // back to the UI thread from the thread-pool read.
        _timeline = new TimelineAggregationService(
            _intelligence, _session, _narrative, _operationHistory, uiDispatcher);

        // Persist each new timeline event to SQLite before the page sees it.
        // EnqueueEvent() is non-blocking (ConcurrentQueue enqueue).
        _timeline.NewEventAdded += (_, ev) => _timelineEventRepo!.EnqueueEvent(ev);

        _timeline.Start(); // must start after narrative (subscribes to NarrativeUpdated)

        // ── Distributed intelligence layer ────────────────────────────────────
        // Created after _timeline.Start() so DistributedTimelineAggregator holds
        // a valid, started ITimelineAggregationService reference.
        // Device identity probe runs lazily off-thread on first page navigation.
        _deviceIdentity = new DeviceIdentityService();
        _trustedDevices = new TrustedDeviceRegistry();
        _localSync      = new LocalSyncCoordinator(
            _deviceIdentity, _trustedDevices, _workloadProfiling, _session, _telemetry);
        _distributedTimeline   = new DistributedTimelineAggregator(
            _deviceIdentity, _localSync, _timeline);
        _crossMachineAnalytics = new CrossMachineAnalyticsEngine(
            _deviceIdentity, _localSync);
        _localSync.Start();

        // ── ExplainMyPC flagship engine ───────────────────────────────────────
        // Must start after narrative and timeline — subscribes to both.
        // Seeds from current state immediately if insights already exist.
        _explainEngine = new ExplainMyPcEngine(
            _intelligence, _timeline, _narrative, _baseline, _history);
        _explainEngine.Start();

        // ── Operational Replay service ────────────────────────────────────────
        // Stateless — no Start()/Stop() required. Created after timeline and
        // history so it can capture a fully-populated snapshot on first request.
        _replayService = new OperationalReplayService(
            _timeline, _history, _operationHistory, _baseline);

        // ── Remediation learning service ──────────────────────────────────────
        // Created after replay service (depends on it for before/after comparisons).
        // Loads persisted outcome records immediately so profiles are available
        // before the first AnalyzePendingActionsAsync pass completes.
        var learningSvc = new RemediationLearningService(
            _operationHistory, _replayService, _outcomeRepo!);
        _ = learningSvc.LoadPersistedProfilesAsync();
        _ = learningSvc.AnalyzePendingActionsAsync();
        _learningService = learningSvc;

        // ── Operational Watchtower ────────────────────────────────────────────
        // Created after learning service so intervention planner has effectiveness
        // profiles available. The coordinator uses historical analytics, learning,
        // and governance to orchestrate the sub-engines.
        _watchtowerCoordinator = new ProactiveRecommendationCoordinator(
            _historicalAnalytics, _learningService, _governance);
        _watchtower = new OperationalWatchtowerService(
            _watchtowerCoordinator, _governance, uiDispatcher);
        _watchtower.Start();

        // ── Anomaly Intelligence layer ─────────────────────────────────────────
        // Created after _baseline, _telemetry, and _watchtower are all running so
        // each service can seed from the current live state immediately.
        _behavioralBaseline    = new BehavioralBaselineService(_baseline!);
        _anomalyDetection      = new AnomalyDetectionService(_baseline!, _telemetry!);
        _driftDetection        = new DriftDetectionService(_watchtower);
        _earlyWarning          = new EarlyWarningService(_anomalyDetection, _watchtower);
        _personalityClassifier = new SystemPersonalityClassifier();

        // ── Autonomous Remediation service ────────────────────────────────────
        // Created after watchtower (depends on the same learning + governance stack).
        // Subscribes to InsightsUpdated to refresh suggestions live.
        // No background timer — suggestions update on insight changes only.
        _remediationService = new AutonomousRemediationService(
            _executionEngine,
            _governance,
            _intelligence,
            _learningService,
            _telemetry,
            uiDispatcher);
        _remediationService.Start();

        // ── Operational Simulation engine ─────────────────────────────────────
        // Stateless — no Start()/Stop() required. Created after all upstream
        // services are running so it can snapshot current state immediately.
        _simulationEngine = new OperationalSimulationEngine(
            _historicalAnalytics,
            _learningService,
            _telemetry,
            _intelligence,
            _baseline,
            _workloadProfiling,
            _governance);

        // ── Simulation history service ────────────────────────────────────────
        // Loads persisted history synchronously in its constructor
        // (file read is fast; history is only used for display, not critical path).
        _simulationHistory = new SimulationHistoryService();

        // ── Outcome verification service ──────────────────────────────────────
        // Loads persisted outcomes synchronously in its constructor.
        // Pending verifications are in-memory only — they do not survive restarts.
        _outcomeVerification = new OutcomeVerificationService();

        // ── Machine health trajectory service ─────────────────────────────────
        // Wraps HistoricalAnalytics + Intelligence to produce a concise report.
        // Stateless — no Start()/Stop() required.
        _healthTrajectory = new MachineHealthTrajectoryService(
            _historicalAnalytics!,
            _intelligence!);

        // ── Adaptive Personalization layer ────────────────────────────────────
        // Created after learning service so effectiveness profiles are available.
        // InterventionMemoryService loads persisted records asynchronously in its constructor.
        // PersonalizationEngine and UserBehaviorClassifier are stateless — no Start/Stop.
        _interventionMemory     = new InterventionMemoryService();
        _personalizationEngine  = new PersonalizationEngine();
        _userBehaviorClassifier = new UserBehaviorClassifier();
        _alertFatigueManager    = new AlertFatigueManager(_interventionMemory);
        _recommendationExplanation = new RecommendationExplanationService();

        // ── Hourly downsampling timer ─────────────────────────────────────────
        // Aggregates raw telemetry into coarser buckets and evicts stale rows.
        // Runs on a thread-pool thread — never touches the UI thread.
        // Idle-only behavior: SQLite WAL means the main thread is never blocked.
        _downsampleTimer = new System.Threading.Timer(
            _ => _ = _telHistoryRepo!.DownsampleAndPurgeAsync(),
            state:        null,
            dueTime:      TimeSpan.FromHours(1),
            period:       TimeSpan.FromHours(1));
    }

    /// <summary>
    /// Stops and disposes all services.
    /// Call from the main window's Closed event handler.
    /// </summary>
    public static void Shutdown()
    {
        // Simulation, history, verification, and trajectory services are stateless — null them.
        _simulationEngine    = null;
        _simulationHistory   = null;
        _outcomeVerification = null;
        _healthTrajectory    = null;

        // Personalization layer — stateless services, just null references.
        _interventionMemory        = null;
        _personalizationEngine     = null;
        _userBehaviorClassifier    = null;
        _alertFatigueManager       = null;
        _recommendationExplanation = null;

        // Stop remediation service first — it holds an InsightsUpdated subscription.
        _remediationService?.Stop();
        _remediationService?.Dispose();
        _remediationService = null;

        // Anomaly Intelligence layer — null before Watchtower and Telemetry stop,
        // since they hold subscriptions to those event sources. The upstream sources
        // are stopped immediately below so no further events will fire.
        _earlyWarning          = null;
        _driftDetection        = null;
        _anomalyDetection      = null;
        _behavioralBaseline    = null;
        _personalityClassifier = null;

        // Stop Watchtower — it holds a SnapshotUpdated subscription.
        _watchtower?.Dispose();
        _watchtower            = null;
        _watchtowerCoordinator = null;

        // Stop distributed layer first — LocalSyncCoordinator holds a ReadingAvailable
        // subscription to telemetry and must release it before telemetry stops.
        _localSync?.Stop();
        _localSync?.Dispose();
        _localSync             = null;
        _distributedTimeline   = null;
        _crossMachineAnalytics = null;
        _trustedDevices        = null;
        _deviceIdentity        = null;

        // Stop diagnostics first — it holds subscriptions to telemetry and governance.
        _diagnostics?.Stop();
        if (_diagnostics is IDisposable dd) dd.Dispose();
        _diagnostics = null;

        // Stop governance — it holds a ReadingAvailable subscription.
        _governance?.Stop();
        if (_governance is IDisposable gd) gd.Dispose();
        _governance         = null;
        _concurrencyBudget  = null;
        _executionQueue     = null;
        _pollingCoordinator = null;

        _executionEngine    = null;
        _executorRegistry   = null;
        _operationHistory   = null;
        _startupManagement  = null;

        // Learning service is stateless after init — just null the reference.
        _learningService = null;

        // Stop the hourly downsampling timer before disposing SQLite.
        _downsampleTimer?.Dispose();
        _downsampleTimer = null;

        // Replay service is stateless — just null the reference.
        _replayService = null;

        // ExplainEngine must stop before timeline and narrative —
        // it holds subscriptions to both.
        _explainEngine?.Stop();
        _explainEngine = null;

        // Timeline must stop before narrative/session/intelligence —
        // it holds subscriptions to all three.
        _timeline?.Stop();
        _timeline = null;

        _narrative?.Stop();
        _narrative = null;

        // Behavioral layer must stop before session (it holds a ReadingAvailable subscription).
        _workloadProfiling?.Dispose();
        _workloadProfiling = null;
        _behavioralContext = null;

        // Session must stop before intelligence — it holds a subscription to InsightsUpdated.
        _session?.Stop();
        _session = null;

        _intelligence?.Stop();
        if (_intelligence is IDisposable id) id.Dispose();
        _intelligence = null;

        _baseline?.Stop();
        _baseline = null;

        _telemetry?.Stop();
        if (_telemetry is IDisposable td) td.Dispose();
        if (_history  is IDisposable hd) hd.Dispose();
        _telemetry = null;
        _history   = null;

        // Flush the SQLite write queue and close the connection.
        // Dispose() calls FlushQueueAsync() synchronously to avoid data loss.
        if (_persistence is not null)
        {
            _persistence.Dispose();
            _persistence = null;
        }
        _telHistoryRepo     = null;
        _timelineEventRepo  = null;
        _insightHistoryRepo = null;
        _outcomeRepo        = null;
        _historicalAnalytics = null;
        _lastInsightIds     = [];
    }
}
