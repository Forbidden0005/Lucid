using Lucid.Core.Infrastructure;
using Lucid.Services.Infrastructure.Composition;
using Lucid.Services.Infrastructure.Events;
using Lucid.Services.Infrastructure.Lifecycle;
using Lucid.Services.Infrastructure.OperationalState;
using Lucid.Services.Diagnostics;
using Lucid.Services.Diagnostics.Logging;
using Lucid.Services;
using Lucid.Services.Analytics;
using Lucid.Services.Autonomy;
using Lucid.Services.Automation;
using Lucid.Services.Chat;
using Lucid.Services.Conversation;
using Lucid.Services.Trust;
using Lucid.Services.LlmChat;
using Lucid.Services.DesktopContext;
using Lucid.Services.Companion;
using Lucid.Services.Reasoning;
using Lucid.Services.Reasoning.Cognitive;
using Lucid.Services.Reasoning.Context;
using Lucid.Services.Reasoning.Memory;
using Lucid.Services.Intelligence.Arbitration;
using Lucid.Services.Workflow;
using Lucid.Services.Baseline;
using Lucid.Services.Behavior;
using Lucid.Services.Cleanup;
using Lucid.Services.Distributed;
using Lucid.Services.Governance;
using Lucid.Services.Learning;
using Lucid.Services.Execution;
using Lucid.Services.Execution.Executors;
using Lucid.Services.Explain;
using Lucid.Services.History;
using Lucid.Services.Intelligence;
using Lucid.Services.Narrative;
using Lucid.Services.Persistence;
using Lucid.Services.Replay;
using Lucid.Services.Security;
using Lucid.Services.Session;
using Lucid.Services.Settings;
using Lucid.Services.Startup;
using Lucid.Services.ProcessIntel;
using Lucid.Services.Storage;
using Lucid.Services.Timeline;
using Lucid.Services.Remediation;
using Lucid.Services.Simulation;
using Lucid.Services.Watchtower;
using Lucid.Services.VisualContext;
using Lucid.Services.Trust.EndpointValidation;
using Lucid.Services.Trust.Integrity;
using Lucid.Services.Infrastructure.Startup;
using Lucid.Services.Persistence.Reliability;
using Lucid.Services.Diagnostics.Governance;
using Lucid.Services.Diagnostics.Reasoning;
using Lucid.Services.Interaction.Presentation;
using Lucid.Services.Interaction.Density;
using Lucid.Services.Interaction.Recommendations;
using Lucid.Services.Interaction.Explainability;
using Lucid.Services.Interaction.Narrative;
using Lucid.Services.Interaction.Attention;
using Lucid.Services.Interaction.Diagnostics;
using Lucid.Services.Intelligence.Patterns;
using Lucid.Services.Intelligence.Baselines;
using Lucid.Services.Intelligence.Outcomes;
using Lucid.Services.Intelligence.Calibration;
using Lucid.Services.Intelligence.Drift;
using Lucid.Services.Intelligence.Learning;
using Lucid.Services.Diagnostics.Learning;
using Microsoft.UI.Dispatching;


namespace Lucid;

/// <summary>
/// Lightweight application-level service registry.
///
/// Provides a straightforward alternative to a full DI container at
/// Lucid's current scale. All services are created once from
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
    // ── Structured logger ─────────────────────────────────────────────────────
    private static ILucidLogger? _logger;

    // ── Phase 18: Unified Operational Core ───────────────────────────────────
    private static IEventBus?                    _eventBus;
    private static ServiceHealthRegistry?        _healthRegistry;
    private static LifecycleCoordinator?         _lifecycleCoordinator;
    private static IOperationalStateCoordinator? _operationalState;
    private static IOperationalLogger?           _operationalLogger;
    private static IPlatformMetricsService?      _platformMetrics;

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
    private static ServiceRegistry?             _registry;
    private static IStartupManagementService?   _startupManagement;
    private static IExplainMyPcEngine?           _explainEngine;
    private static ProcessIntelligenceService?   _processIntelligence;
    private static IOperationalReplayService?    _replayService;
    private static IRemediationLearningService?  _learningService;
    private static RollbackStagingMaintenanceService? _rollbackMaintenance;

    // ── SQLite persistence layer ──────────────────────────────────────────────
    private static SQLitePersistenceService?        _persistence;
    private static HistoricalTelemetryRepository?   _telHistoryRepo;
    private static TimelineEventRepository?         _timelineEventRepo;
    private static InsightHistoryRepository?        _insightHistoryRepo;
    private static RecommendationOutcomeRepository? _outcomeRepo;
    private static IHistoricalAnalyticsEngine?      _historicalAnalytics;
    private static HashSet<string>                  _lastInsightIds = [];
    private static TelemetryRetentionService?       _telemetryRetention;

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

    // ── Operational Evidence & Workflow layer ─────────────────────────────────
    private static IOperationalEvidenceGraph?   _evidenceGraph;
    private static IRootCauseAnalysisEngine?    _rootCauseEngine;
    private static IEvidenceExplanationService? _evidenceExplanation;
    private static IOperationalWorkflowEngine?  _workflowEngine;

    // ── Operational Companion layer ────────────────────────────────────────────
    private static ICompanionSessionManager?        _companionSession;
    private static IOperationalConversationEngine?  _conversationEngine;
    private static IOperationalConversationService? _conversationService;
    private static ILlmChatService?                 _llmChat;

    // ── Home chat surface ─────────────────────────────────────────────────────
    private static ILlmChatService?   _homeChat;
    private static IChatSessionStore? _chatSessions;

    // ── Desktop Context layer ─────────────────────────────────────────────────
    private static DesktopContextService? _desktopContext;

    // ── Desktop Automation layer (Phase 17D) ──────────────────────────────────
    private static AutomationOrchestrator? _automationOrchestrator;

    // ── Operational Trust layer (Phase 17E) ───────────────────────────────────
    private static AutomationAuditService?       _automationAudit;
    private static ConsentExplanationService?    _consentExplanation;
    private static AutomationTransparencyEngine? _automationTransparency;
    private static AutomationConsentService?     _automationConsent;
    private static OperationalTrustManager?      _trustManager;

    // ── Autonomous Workflow Execution layer (Phase 17F) ───────────────────────
    private static SafeExecutionValidator?          _safeExecutionValidator;
    private static ExecutionNarrativeService?       _executionNarrative;
    private static OperationalGoalResolver?         _goalResolver;
    private static WorkflowCheckpointManager?       _checkpointManager;
    private static HumanReviewGate?                 _humanReviewGate;
    private static FileOrganizationWorkflowService? _fileOrgWorkflow;
    private static OperationalFileDiscoveryService? _fileDiscovery;
    private static WorkflowExecutionPlanner?        _workflowPlanner;
    private static TaskCompletionCoordinator?       _taskCoordinator;
    private static AutonomousWorkflowEngine?        _autonomousWorkflowEngine;

    // ── Phase 18C: Trust & Governance Hardening ──────────────────────────────
    private static TrustIntegrityService?          _trustIntegrity;
    private static ConsentIntegrityValidator?      _consentIntegrityValidator;
    private static TrustPostureRecoveryManager?    _trustPostureRecovery;
    private static GovernanceDiagnosticsService?   _governanceDiagnostics;
    private static PersistenceHealthMonitor?       _persistenceMonitor;

    // ── Phase 19: Unified Cognitive Reasoning Layer ───────────────────────────
    private static IOperationalContextSynthesizer? _contextSynthesizer;
    private static IReasoningMemoryService?        _reasoningMemory;
    private static IRecommendationArbitrator?      _arbitrator;
    private static ICognitiveReasoningEngine?      _cognitiveReasoning;

    // ── Phase 19 gap: cognitive observability ────────────────────────────────
    private static ReasoningDiagnosticsService?  _reasoningDiagnostics;
    private static CognitiveHealthMetrics?       _cognitiveMetrics;
    private static RecommendationOutcomeTracker? _outcomeTracker;
    private static PatternConsistencyAnalyzer?   _patternAnalyzer;

    // ── Phase 21: Adaptive Operational Learning & Pattern Intelligence ────────
    private static PatternIntelligenceEngine?       _patternIntelligence;
    private static AdaptiveBaselineTracker?         _adaptiveBaselines;
    private static RecommendationEffectivenessAnalyzer? _effectivenessAnalyzer;
    private static LearningOutcomeService?          _learningOutcomeService;
    private static ConfidenceCalibrationEngine?     _calibrationEngine;
    private static LongitudinalBehaviorTracker?     _longitudinalTracker;
    private static GovernanceLearningPolicy?        _governanceLearningPolicy;
    private static AdaptiveLearningMetrics?         _adaptiveLearningMetrics;
    private static LearningDiagnosticsService?      _learningDiagnostics;

    // ── Phase 20: Unified Human Interaction & Cognitive UX Layer ─────────────
    private static CognitivePresentationService?    _cognitivePresentationService;
    private static InformationDensityCoordinator?   _densityCoordinator;
    private static UnifiedRecommendationService?    _unifiedRecommendationService;
    private static ExplainabilityRenderer?          _explainabilityRenderer;
    private static CognitiveTimelineBuilder?        _cognitiveTimelineBuilder;
    private static AttentionCoordinator?            _attentionCoordinator;
    private static InteractionHealthMetrics?        _interactionMetrics;
    private static InteractionDiagnosticsService?   _interactionDiagnostics;

    // ── Semantic Desktop Understanding layer (Phase 18B) ─────────────────────
    private static ConsentBoundScreenAnalysis?  _visualConsent;
    private static ScreenCaptureCoordinator?    _screenCapture;
    private static WindowSemanticAnalyzer?      _windowAnalyzer;
    private static ExplorerVisualInterpreter?   _explorerInterpreter;
    private static SettingsPageInterpreter?     _settingsInterpreter;
    private static VisualWorkflowLocator?       _workflowLocator;
    private static VisualContextService?        _visualContext;

    // ── Settings layer ────────────────────────────────────────────────────────
    private static ISettingsService? _settings;

    // ── Service accessors ─────────────────────────────────────────────────────

    /// <summary>
    /// Structured file-backed logger. Available from the first line of
    /// Initialize() — safe to call from any service or background thread.
    /// </summary>
    public static ILucidLogger Logger =>
        _logger ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    // ── Phase 18 accessors ────────────────────────────────────────────────────

    /// <summary>
    /// Strongly-typed internal event bus. Use to publish and subscribe to
    /// operational events without direct service-to-service coupling.
    /// </summary>
    public static IEventBus EventBus =>
        _eventBus ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Platform service health registry.</summary>
    public static ServiceHealthRegistry HealthRegistry =>
        _healthRegistry ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Lifecycle coordinator — manages phased startup and shutdown
    /// for services that opt in to <see cref="IServiceLifecycleParticipant"/>.
    /// </summary>
    public static LifecycleCoordinator LifecycleCoordinator =>
        _lifecycleCoordinator ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Unified operational state snapshot. Read <see cref="IOperationalStateCoordinator.Current"/>
    /// for the latest platform posture or subscribe to <see cref="IOperationalStateCoordinator.StateChanged"/>.
    /// </summary>
    public static IOperationalStateCoordinator OperationalState =>
        _operationalState ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Operational logger with correlation context and in-memory capture
    /// for the Diagnostics page.
    /// </summary>
    public static IOperationalLogger OperationalLogger =>
        _operationalLogger ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Internal platform health and performance metrics.</summary>
    public static IPlatformMetricsService PlatformMetrics =>
        _platformMetrics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    // ── Phase 19 accessors ─────────────────────────────────────────────────────

    /// <summary>
    /// Synthesizes current workload context from telemetry and process signals.
    /// Used by the cognitive reasoning engine and recommendation arbitrator.
    /// </summary>
    public static IOperationalContextSynthesizer ContextSynthesizer =>
        _contextSynthesizer ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Lightweight in-memory inference history. Tracks pattern recurrence,
    /// validation outcomes, and confidence calibration inputs.
    /// </summary>
    public static IReasoningMemoryService ReasoningMemory =>
        _reasoningMemory ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Recommendation arbitrator — deduplicates, ranks, and clusters
    /// recommended actions through the prioritization + fatigue + context pipeline.
    /// </summary>
    public static IRecommendationArbitrator RecommendationArbitrator =>
        _arbitrator ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Cognitive reasoning engine. Call <c>AnalyzeAsync()</c> to run a
    /// cross-domain inference cycle and get contextual operational conclusions.
    /// </summary>
    public static ICognitiveReasoningEngine CognitiveReasoning =>
        _cognitiveReasoning ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    // ── Phase 19 gap: cognitive observability ─────────────────────────────────

    /// <summary>
    /// Thread-safe runtime metrics for the cognitive reasoning pipeline.
    /// Tracks cycles, inferences, arbitration decisions, suppression rates,
    /// and average confidence. Updated after every AnalyzeAsync() call.
    /// </summary>
    public static CognitiveHealthMetrics CognitiveMetrics =>
        _cognitiveMetrics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Reasoning diagnostics service — collects cognitive cycle traces and
    /// arbitration decision records. Exposes the last 50 cycles and 500 decisions.
    /// Publishes CognitiveTraceCommittedEvent after each cycle for live UI refresh.
    /// </summary>
    public static ReasoningDiagnosticsService ReasoningDiagnostics =>
        _reasoningDiagnostics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Tracks accept/reject/dismiss/expiry outcomes for recommended actions.
    /// Feeds the personalization and pattern consistency layers.
    /// Local-only, in-memory (max 50 outcomes per action ID).
    /// </summary>
    public static RecommendationOutcomeTracker OutcomeTracker =>
        _outcomeTracker ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Analyzes historical inference patterns to compute per-inference consistency
    /// boosts/penalties. Stateless — pass the current inference and history snapshot.
    /// </summary>
    public static PatternConsistencyAnalyzer PatternAnalyzer =>
        _patternAnalyzer ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    // ── Phase 21: Adaptive Operational Learning & Pattern Intelligence ─────────

    /// <summary>Pattern intelligence engine — detects recurring operational patterns.</summary>
    public static PatternIntelligenceEngine PatternIntelligence =>
        _patternIntelligence ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Adaptive baseline tracker — per-workload-type baseline profiles.</summary>
    public static AdaptiveBaselineTracker AdaptiveBaselines =>
        _adaptiveBaselines ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Recommendation effectiveness analyzer — outcome-grounded usefulness scoring.</summary>
    public static RecommendationEffectivenessAnalyzer EffectivenessAnalyzer =>
        _effectivenessAnalyzer ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Learning outcome service — unified outcome coordinator.</summary>
    public static LearningOutcomeService LearningOutcomes =>
        _learningOutcomeService ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Confidence calibration engine — adaptive accuracy tracking and drift detection.</summary>
    public static ConfidenceCalibrationEngine CalibrationEngine =>
        _calibrationEngine ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Longitudinal behavior tracker — extended time-window metric history.</summary>
    public static LongitudinalBehaviorTracker LongitudinalTracker =>
        _longitudinalTracker ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Governance learning policy — evaluates learning permission from consent posture.</summary>
    public static GovernanceLearningPolicy LearningPolicy =>
        _governanceLearningPolicy ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Adaptive learning metrics — pattern recognition and calibration counters.</summary>
    public static AdaptiveLearningMetrics AdaptiveLearningMetrics =>
        _adaptiveLearningMetrics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Learning diagnostics service — adaptive learning audit trail.</summary>
    public static LearningDiagnosticsService LearningDiagnostics =>
        _learningDiagnostics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    // ── Phase 20: Unified Human Interaction & Cognitive UX Layer ─────────────

    /// <summary>
    /// Converts cognitive engine output and insight findings into
    /// policy-compliant, fully-formatted presentation snapshots for ViewModels.
    /// </summary>
    public static CognitivePresentationService CognitivePresentation =>
        _cognitivePresentationService ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Tracks the recommended information density level based on system load
    /// and active inference count. Supports manual override for diagnostic panels.
    /// </summary>
    public static InformationDensityCoordinator DensityCoordinator =>
        _densityCoordinator ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Transforms arbitration clusters into lifecycle-tracked
    /// <see cref="UnifiedRecommendation"/> records with formatted display text.
    /// </summary>
    public static UnifiedRecommendationService UnifiedRecommendations =>
        _unifiedRecommendationService ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Renders inference explanations, confidence breakdowns, and arbitration
    /// narratives for the Explainability UX panel.
    /// </summary>
    public static ExplainabilityRenderer ExplainabilityRenderer =>
        _explainabilityRenderer ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Builds narrative window summaries and 15-minute bucket groups
    /// from the timeline event stream.
    /// </summary>
    public static CognitiveTimelineBuilder CognitiveTimeline =>
        _cognitiveTimelineBuilder ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Central attention gatekeeper — evaluates whether a recommendation should
    /// surface now given budget, cooldowns, workload, and fatigue state.
    /// </summary>
    public static AttentionCoordinator AttentionCoordinator =>
        _attentionCoordinator ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Interaction-layer health metrics: suppression rates, render latency, etc.</summary>
    public static InteractionHealthMetrics InteractionMetrics =>
        _interactionMetrics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>
    /// Interaction diagnostics service — records suppression events and publishes
    /// diagnostic events for the "Why was this hidden?" transparency panel.
    /// </summary>
    public static InteractionDiagnosticsService InteractionDiagnostics =>
        _interactionDiagnostics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    // ── Phase 18C: Trust & Governance ────────────────────────────────────────

    /// <summary>Governance audit trail and trust-event publisher.</summary>
    public static GovernanceDiagnosticsService GovernanceDiagnostics =>
        _governanceDiagnostics ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Consent posture integrity validator — HMAC-backed tamper detection.</summary>
    public static TrustPostureRecoveryManager TrustPostureRecovery =>
        _trustPostureRecovery ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called.");

    /// <summary>Persistence write-queue health metrics.</summary>
    public static PersistenceHealthMonitor? PersistenceMonitor => _persistenceMonitor;

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
    /// Composition seam for the strangler migration off this static locator
    /// (ROADMAP C4). Pages migrated off <c>AppServices</c> resolve their
    /// ViewModels from here via <c>App.Registry</c>; one factory is
    /// registered per migrated ViewModel at the end of
    /// <see cref="Initialize"/>. New code must use this seam — the debt
    /// ratchet (scripts/check-debt-ratchet.ps1) fails CI on any new file
    /// referencing <c>AppServices</c> directly.
    /// </summary>
    public static IServiceRegistry Registry =>
        _registry ?? throw new InvalidOperationException(
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
    /// Lucid operational reasoning engine — the flagship feature.
    /// Synthesizes insights, timeline events, narrative, and baseline data into
    /// a single coherent OperationalExplanation that drives the Explain My PC page.
    /// </summary>
    public static IExplainMyPcEngine ExplainEngine =>
        _explainEngine ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Per-process intelligence: live CPU/RAM, anomaly detection, and behavioral
    /// classification. Publishes a new snapshot each telemetry tick.
    /// Use <see cref="ProcessIntelligenceService.LastSnapshot"/> for the latest data,
    /// or subscribe to <see cref="ProcessIntelligenceService.SnapshotUpdated"/> for live updates.
    /// </summary>
    public static ProcessIntelligenceService ProcessIntelligence =>
        _processIntelligence ?? throw new InvalidOperationException(
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
    /// Insight onset/resolution history repository.
    /// Read by the insight detail page to show historical context for findings
    /// that are no longer active in the live intelligence engine (e.g. rows
    /// deep-linked from the health-score breakdown).
    /// </summary>
    public static InsightHistoryRepository InsightHistory =>
        _insightHistoryRepo ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Runtime governance service.
    /// Monitors system pressure (CPU, GPU, battery, thermal) and manages
    /// concurrency budgets, adaptive polling rates, and workload deferral.
    /// All services that run heavy background work consult this service
    /// before starting to ensure Lucid stays lightweight.
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
    /// %LOCALAPPDATA%\Lucid\simulation_history.json.
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
    /// %LOCALAPPDATA%\Lucid\simulation_outcomes.json (max 50).
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
    /// Persists to %LOCALAPPDATA%\Lucid\intervention_memory.json.
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

    /// <summary>
    /// Operational evidence graph service.
    /// Builds causal evidence chains from intelligence findings, anomalies, drifts,
    /// session events, and operation history. Local-only, deterministic.
    /// </summary>
    public static IOperationalEvidenceGraph EvidenceGraph =>
        _evidenceGraph ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Root cause analysis engine.
    /// Identifies the most probable root causes from the evidence graph.
    /// Pure function — stateless, no side effects.
    /// </summary>
    public static IRootCauseAnalysisEngine RootCauseEngine =>
        _rootCauseEngine ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Evidence explanation service.
    /// Generates human-readable explanations for root cause candidates and evidence chains.
    /// Pure function — stateless, no side effects.
    /// </summary>
    public static IEvidenceExplanationService EvidenceExplanation =>
        _evidenceExplanation ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Operational workflow engine.
    /// Builds guided, evidence-driven workflows from root cause candidates.
    /// Never auto-executes anything — all workflows are suggestions only.
    /// </summary>
    public static IOperationalWorkflowEngine WorkflowEngine =>
        _workflowEngine ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Companion session state manager.
    /// Owns the overlay visibility state (Hidden / Bubble / Expanded) and pin state.
    /// Does NOT own conversation messages — those live in CompanionChatViewModel.
    /// Ephemeral — state is runtime-only, nothing is persisted to disk.
    /// </summary>
    public static ICompanionSessionManager CompanionSession =>
        _companionSession ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Operational conversation engine (legacy interface).
    /// Use <see cref="ConversationService"/> for the richer Phase 17C interface.
    /// </summary>
    public static IOperationalConversationEngine ConversationEngine =>
        _conversationEngine ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Operational conversation service (Phase 17C).
    /// Processes queries into structured OperationalResponse objects with evidence,
    /// confidence, suggested actions, and contextual suggestions.
    /// Deterministic keyword matching — not an LLM. Local-only.
    /// </summary>
    public static IOperationalConversationService ConversationService =>
        _conversationService ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// LLM-powered conversational chat service.
    /// Backed by local Ollama inference — endpoint and model are user-configurable
    /// via Settings → AI Assistant. Free, local, no cloud.
    /// Injects live system data into every message via <see cref="LlmSystemContextBuilder"/>.
    /// </summary>
    public static ILlmChatService LlmChat =>
        _llmChat ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Chat service for the home page (<c>ChatPage</c>).
    ///
    /// A second instance rather than a second consumer of <see cref="LlmChat"/>:
    /// the home page and the floating companion overlay show different
    /// conversations, and conversation history is per-service state. Sharing one
    /// would mean resuming a saved session on the home page silently wiped the
    /// context the overlay was mid-conversation in. Same local endpoint, same
    /// model, same live system prompt — only the history is separate.
    /// </summary>
    public static ILlmChatService HomeChat =>
        _homeChat ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Storage for the home page's saved conversations.
    ///
    /// Currently process-lifetime (<see cref="InMemoryChatSessionStore"/>): the
    /// rail is fully functional within a run, but conversations do not survive a
    /// restart. The durable implementation belongs in the SQLite persistence
    /// layer as a schema migration; nothing outside this registration needs to
    /// change when it lands.
    /// </summary>
    public static IChatSessionStore ChatSessions =>
        _chatSessions ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Desktop context awareness service.
    /// Observes the active foreground window, File Explorer path, and clipboard
    /// metadata via Win32 APIs. Observation-only — no automation, no screenshots.
    /// All context is ephemeral; nothing is persisted to disk.
    /// </summary>
    public static IDesktopContextService DesktopContext =>
        _desktopContext ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Desktop Automation Orchestrator (Phase 17D).
    /// Builds and executes guided, consent-driven automation plans.
    /// NEVER auto-executes without explicit user approval.
    /// All execution is narrated, interruptible, and logged to the timeline.
    /// </summary>
    public static AutomationOrchestrator AutomationOrchestrator =>
        _automationOrchestrator ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Append-oriented audit ledger for all automation consent decisions and outcomes (Phase 17E).
    /// Persists to %LOCALAPPDATA%\Lucid\audit-log.json. Retained for 30 days.
    /// </summary>
    public static AutomationAuditService AutomationAudit =>
        _automationAudit ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Consent explanation service (Phase 17E).
    /// Generates plain-English "what changes" / "how to undo" text for consent cards.
    /// Pure — stateless, no side effects.
    /// </summary>
    public static ConsentExplanationService ConsentExplanation =>
        _consentExplanation ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Automation transparency engine (Phase 17E).
    /// Produces causality-exposing "why suggested" narration for consent cards.
    /// Pure — stateless, no side effects.
    /// </summary>
    public static AutomationTransparencyEngine AutomationTransparency =>
        _automationTransparency ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Central consent authority for automation actions (Phase 17E).
    /// All actions that modify system state must request consent here before executing.
    /// Enforces the boundary policy, consent mode, and audit trail.
    /// </summary>
    public static AutomationConsentService AutomationConsent =>
        _automationConsent ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Operational trust posture manager (Phase 17E).
    /// Tracks session-level trust history and adapts narration tone and
    /// consent card prominence based on recent consent patterns.
    /// </summary>
    public static OperationalTrustManager TrustManager =>
        _trustManager ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Autonomous workflow engine (Phase 17F).
    /// Resolves conversational requests to operational goals, plans multi-step
    /// workflows, and executes them with human review gates and safety validation.
    /// NEVER executes file operations without explicit user approval.
    /// </summary>
    public static AutonomousWorkflowEngine AutonomousWorkflowEngine =>
        _autonomousWorkflowEngine ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Human review gate (Phase 17F).
    /// Raises approval cards for workflow steps that require user confirmation.
    /// </summary>
    public static HumanReviewGate HumanReviewGate =>
        _humanReviewGate ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Operational goal resolver (Phase 17F).
    /// Maps plain-text requests to structured OperationalGoal objects.
    /// </summary>
    public static OperationalGoalResolver GoalResolver =>
        _goalResolver ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Visual context service (Phase 18B).
    /// Provides consent-gated semantic understanding of the visible desktop.
    /// All analysis is explicit, local-only, and auto-expiring.
    /// </summary>
    public static IVisualContextService VisualContext =>
        _visualContext ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// Consent gate for visual analysis (Phase 18B).
    /// Subscribe ConsentRequired in the companion overlay to show approval cards.
    /// </summary>
    public static ConsentBoundScreenAnalysis VisualConsent =>
        _visualConsent ?? throw new InvalidOperationException(
            "AppServices.Initialize() has not been called. " +
            "Call it from App.OnLaunched before creating the main window.");

    /// <summary>
    /// User settings service.
    /// Loads from %LocalAppData%\Lucid\settings.json on startup and writes
    /// atomically on every Save call.  Defaults are used on first run.
    /// </summary>
    public static ISettingsService Settings =>
        _settings ?? throw new InvalidOperationException(
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
        // ── Structured logger ─────────────────────────────────────────────────
        // Created before everything else so all subsequent init steps can log.
        _logger = new LucidLogger();
        _logger.Info("Startup", "AppServices.Initialize() started");

        // ── UI-thread dispatch adapter ────────────────────────────────────────
        // Services that live in Lucid.Core cannot reference DispatcherQueue, so
        // they take IUiDispatcher instead. One adapter is built here and shared
        // by every consumer — see Lucid.Core/Services/Infrastructure/IUiDispatcher.cs.
        var ui = new DispatcherQueueUiDispatcher(uiDispatcher);

        // ── Phase 18: Unified Operational Core ────────────────────────────────
        // These services form the platform's nervous system.
        // Initialized immediately after the logger so every subsequent
        // service can publish events, report state, and log with correlation.

        _eventBus             = new LucidEventBus();
        _healthRegistry       = new ServiceHealthRegistry();
        _lifecycleCoordinator = new LifecycleCoordinator(_healthRegistry, _logger);
        _operationalState     = new OperationalStateCoordinator();
        _operationalLogger    = new OperationalLogger(_logger);
        _platformMetrics      = new PlatformMetricsService();

        _logger.Info("Startup", "Phase 18 Unified Operational Core initialized");

        // ── Settings ──────────────────────────────────────────────────────────
        // Loaded first — all downstream services can immediately read persisted
        // preferences. File read is synchronous and fast (< 5 ms). Defaults are
        // used silently on first run or if the file is corrupt.
        _settings = new JsonSettingsStore();

        // ── SQLite persistence layer ──────────────────────────────────────────
        // Initialised first so repositories are ready before any services start
        // and before telemetry events begin firing.
        // StartupTimeoutGuard.Run() offloads onto the thread-pool to avoid
        // deadlocking the WinUI sync context while blocking on the async init.
        var dbService = new SQLitePersistenceService(_logger);
        var dbSnapshot = StartupTimeoutGuard.Run(
            "SQLite initialization",
            () => dbService.InitializeAsync(),
            TimeSpan.FromSeconds(10));
        if (!dbSnapshot.Succeeded)
            _logger.Warning("Startup", $"SQLite init degraded: {dbSnapshot.ErrorMessage}");

        // Wire in the persistence health monitor so queue overflows are visible
        // via the event bus rather than silently dropped.
        var persistenceMetrics = dbService.QueueMetrics;
        _persistenceMonitor = new PersistenceHealthMonitor(persistenceMetrics, _eventBus!);
        dbService.OnWriteDropped = (depth, max) => _persistenceMonitor.OnWriteDropped(depth, max);
        _telHistoryRepo    = new HistoricalTelemetryRepository(dbService);
        _timelineEventRepo = new TimelineEventRepository(dbService);
        _insightHistoryRepo = new InsightHistoryRepository(dbService);

        // Close insight rows orphaned by the previous session (open-row tracking
        // is in-memory, so anything still open at startup can never resolve and
        // would read as permanently "active" in the health breakdown). Runs
        // before the intelligence engine produces this session's first insights;
        // the sessionStart guard protects any row this session creates meanwhile.
        var orphanSweepStart = DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                int closed = await _insightHistoryRepo.CloseOrphanedRowsAsync(orphanSweepStart)
                    .ConfigureAwait(false);
                if (closed > 0)
                    _logger?.Info("Persistence",
                        $"Closed {closed} insight-history row(s) left open by a previous session");
            }
            catch (Exception ex)
            {
                _logger?.Warning("Persistence", $"Orphaned insight-row sweep failed: {ex.Message}");
            }
        });

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

        // Startup management must be created before the telemetry service so it
        // can be injected — the telemetry service uses GetAllEntries() to correctly
        // exclude stale registry entries and Task-Manager-disabled apps.
        _startupManagement = new StartupManagementService();

        _telemetry = new WindowsTelemetryService(uiDispatcher, _startupManagement);
        _history   = new TelemetryHistoryBuffer();

        // Register the telemetry service as the adaptive target so the governance
        // layer can adjust polling intervals when the runtime mode changes.
        _pollingCoordinator.RegisterTarget((WindowsTelemetryService)_telemetry);

        // Adaptive baseline service — learns this machine's normal operating
        // ranges over time and persists them to %LOCALAPPDATA%\Lucid\.
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

            // Null-guard: _intelligence.Stop() prevents new InsightsUpdated
            // events from telemetry, but guard defensively in case the engine
            // fires a final event during the teardown sequence.
            if (_insightHistoryRepo is null) return;

            foreach (var insight in insights)
            {
                if (!_lastInsightIds.Contains(insight.Id))
                    _ = _insightHistoryRepo.RecordOnsetAsync(insight);
            }
            foreach (var oldId in _lastInsightIds)
            {
                if (!newIds.Contains(oldId))
                    _ = _insightHistoryRepo.RecordResolutionAsync(oldId);
            }
            _lastInsightIds = newIds;
        };

        // Every snapshot flows into the history buffer automatically.
        // ReadingAvailable fires on the UI thread, so Record() is always
        // called from a single thread — the write lock is still held for
        // correctness when background analysis threads read concurrently.
        // Null-guard: if a DispatcherQueue-enqueued event fires after Shutdown()
        // nulls _history, skip the record rather than throwing NRE.
        _telemetry.ReadingAvailable += (_, snapshot) => _history?.Record(snapshot);

        // Persist one sample every 30 seconds to the SQLite telemetry table.
        // EnqueueSample() is throttled internally; extra calls are no-ops.
        // Null-guard: same shutdown-window protection as above.
        _telemetry.ReadingAvailable += (_, snapshot) => _telHistoryRepo?.EnqueueSample(
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
            _telemetry, _pollingCoordinator!, _concurrencyBudget!, _executionQueue!,
            ui, new WinRtPowerStateProvider());
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
        // Use null-conditional to guard the shutdown window: AppServices.Shutdown()
        // nulls _diagnostics and _pollingCoordinator before calling _telemetry.Stop(),
        // so a ReadingAvailable event that fires during the teardown sequence would
        // otherwise cause a NullReferenceException.
        _telemetry.ReadingAvailable += (_, _) =>
            _diagnostics?.OnTelemetryReceived(
                DateTimeOffset.Now,
                _pollingCoordinator?.CurrentTelemetryInterval ?? TimeSpan.FromSeconds(1.5));

        // Wire governance mode-change notifications to diagnostics.
        // Same null-guard rationale: _diagnostics is nulled before _governance.Stop().
        _governance.ModeChanged += (_, args) =>
            _diagnostics?.OnGovernanceModeChanged(args.PreviousMode, args.NewMode, args.Reasons);

        _diagnostics.Start();

        // ── Operational history ───────────────────────────────────────────────
        // Initialised before the execution engine so it is ready the moment
        // the first action completes. JSON file created lazily on first write.
        _operationHistory = new OperationHistoryService(_logger);

        // ── Remediation execution engine ──────────────────────────────────────
        _executorRegistry = new ActionExecutorRegistry();
        _executorRegistry.RegisterAll([
            // ── Guided navigation (Phase 1) ───────────────────────────────────
            // Open the relevant system tool — completely safe, no modification.
            new OpenTaskManagerExecutor(),      // action.cpu.open-task-manager
            new OpenTaskManagerGpuExecutor(),   // action.gpu.inspect-processes (GPU tab)
            new OpenTaskSchedulerExecutor(),    // action.cpu.open-task-scheduler
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
        // Null-guard: _timeline.Stop() prevents new events, but guard defensively.
        _timeline.NewEventAdded += (_, ev) => _timelineEventRepo?.EnqueueEvent(ev);

        _timeline.Start(); // must start after narrative (subscribes to NarrativeUpdated)

        // ── Process intelligence layer ────────────────────────────────────────
        // Enriches raw ProcessSamples with per-process behavioral analysis,
        // anomaly detection, and timeline event emission.
        // Created after _timeline.Start() so anomaly events are persisted.
        // The concrete cast is safe — _timeline is always TimelineAggregationService.
        _processIntelligence = new ProcessIntelligenceService(
            _telemetry,
            uiDispatcher,
            (TimelineAggregationService?)_timeline);
        _processIntelligence.Start();

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

        // OPT-IN gate (local-first doctrine): the sync coordinator opens a LAN
        // TCP listener and broadcasts UDP discovery beacons. By default nothing
        // leaves — or listens on — this machine; loops only start when the user
        // has explicitly enabled device sync in Settings.
        if (_settings!.Current.DeviceSyncEnabled)
        {
            _localSync.Start();
            _logger.Info("Startup", "Local device sync active (user opt-in)");
        }
        else
        {
            _logger.Info("Startup",
                "Local device sync off — no LAN discovery beacons or listener " +
                "(opt-in available in Settings)");
        }

        // ── Operational Replay service ────────────────────────────────────────
        // Stateless — no Start()/Stop() required. Created after timeline and
        // history so it can capture a fully-populated snapshot on first request.
        // SQLite repositories are passed to enable extended windows (1h/6h/24h).
        _replayService = new OperationalReplayService(
            _timeline, _history, _operationHistory, _baseline,
            _telHistoryRepo, _timelineEventRepo);

        // ── Remediation learning service ──────────────────────────────────────
        // Created after replay service (depends on it for before/after comparisons).
        // Loads persisted outcome records immediately so profiles are available
        // before the first AnalyzePendingActionsAsync pass completes.
        var learningSvc = new RemediationLearningService(
            _operationHistory, _replayService, _outcomeRepo!);
        _ = learningSvc.LoadPersistedProfilesAsync();
        _ = learningSvc.AnalyzePendingActionsAsync();
        _learningService = learningSvc;

        // ── Rollback staging maintenance ──────────────────────────────────────
        // Reversible cleanups move files into %LOCALAPPDATA%\Lucid\Rollback rather
        // than deleting them. Without a sweeper those sets accumulate forever and
        // the reclaimed space is never truly freed. This IdleOnly service prunes
        // sets past the retention window, governed so it only runs when calm.
        _rollbackMaintenance = new RollbackStagingMaintenanceService(
            _settings!, _governance, _operationalLogger!);
        _rollbackMaintenance.Start();

        // ── Operational Watchtower ────────────────────────────────────────────
        // Created after learning service so intervention planner has effectiveness
        // profiles available. The coordinator uses historical analytics, learning,
        // and governance to orchestrate the sub-engines.
        _watchtowerCoordinator = new ProactiveRecommendationCoordinator(
            _historicalAnalytics, _learningService, _governance);
        _watchtower = new OperationalWatchtowerService(
            _watchtowerCoordinator, _governance, ui);
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

        // ── Phase 19: Unified Cognitive Reasoning Layer ───────────────────────
        // Context synthesizer classifies the current workload from telemetry signals.
        // Reasoning memory tracks inference history for pattern detection and confidence calibration.
        // Recommendation arbitrator composes existing prioritizer + fatigue + context suppression.
        // Cognitive reasoning engine synthesizes all signals into higher-level operational conclusions.
        var globalPrioritizer = new GlobalRecommendationPrioritizer();
        _contextSynthesizer = new OperationalContextSynthesizer(
            _operationalState!);
        _reasoningMemory    = new ReasoningMemoryService();
        _arbitrator         = new RecommendationArbitrator(
            globalPrioritizer,
            _alertFatigueManager!,
            _learningService!);

        // Phase 19 gap: cognitive observability — created before the engine so
        // they can be injected directly (avoid a two-phase init dance).
        _cognitiveMetrics     = new CognitiveHealthMetrics();
        _reasoningDiagnostics = new ReasoningDiagnosticsService(_eventBus!);
        _outcomeTracker       = new RecommendationOutcomeTracker();
        _patternAnalyzer      = new PatternConsistencyAnalyzer();

        // Governance diagnostics must exist BEFORE its consumers below:
        // CognitiveReasoningEngine and GovernanceLearningPolicy inject it
        // directly. It previously lived in the Phase 18C block (~150 lines
        // later), so both consumers silently received null — the classic
        // hand-ordered-registry hazard. Only depends on the event bus.
        _governanceDiagnostics = new GovernanceDiagnosticsService(_eventBus!);

        _cognitiveReasoning = new CognitiveReasoningEngine(
            _intelligence!,
            _operationalState!,
            _contextSynthesizer,
            _reasoningMemory,
            _eventBus!,
            _operationalLogger!,
            _session!,
            _governanceDiagnostics,
            _reasoningDiagnostics,
            _cognitiveMetrics);

        _logger.Info("Startup", "Phase 19 Unified Cognitive Reasoning Layer initialized");

        // ── Lucid flagship engine ─────────────────────────────────────────────
        // Placed here (after Phase 19) so the cognitive engine is available for
        // the first Rebuild() cycle. Subscribes to narrative and timeline; seeds
        // immediately if insights already exist.
        _explainEngine = new ExplainMyPcEngine(
            _intelligence, _timeline, _narrative, _baseline, _history, _cognitiveReasoning);
        _explainEngine.Start();

        // ── Phase 20: Unified Human Interaction & Cognitive UX Layer ──────────
        // All services are created after Phase 19 so they can inject the cognitive engine.
        // Static language/formatting utilities (OperationalLanguagePolicy, SeverityNarrativeEngine,
        // ConfidenceToneAdjuster, CalmCommunicationFormatter) require no registration.
        _interactionMetrics           = new InteractionHealthMetrics();
        _cognitivePresentationService = new CognitivePresentationService(
            _cognitiveReasoning!, _intelligence!);
        _densityCoordinator           = new InformationDensityCoordinator(
            _operationalState!, _cognitiveReasoning!);
        _unifiedRecommendationService = new UnifiedRecommendationService(
            _arbitrator!, _alertFatigueManager!);
        _explainabilityRenderer       = new ExplainabilityRenderer(_recommendationExplanation);
        _cognitiveTimelineBuilder     = new CognitiveTimelineBuilder(_timeline!);
        _attentionCoordinator         = new AttentionCoordinator(
            _alertFatigueManager!, _operationalState!);
        _interactionDiagnostics       = new InteractionDiagnosticsService(
            _eventBus!, _interactionMetrics);

        _logger.Info("Startup", "Phase 20 Unified Human Interaction & Cognitive UX Layer initialized");

        // ── Phase 21: Adaptive Operational Learning & Pattern Intelligence ────
        _adaptiveLearningMetrics  = new AdaptiveLearningMetrics();
        _learningDiagnostics      = new LearningDiagnosticsService(_eventBus!);
        _patternIntelligence      = new PatternIntelligenceEngine();
        // Load cross-session inference history so patterns span multiple sessions.
        // Fire-and-forget — analysis works immediately with an empty history on cold start.
        _ = _patternIntelligence.LoadPersistedHistoryAsync();
        _adaptiveBaselines        = new AdaptiveBaselineTracker();
        _effectivenessAnalyzer    = new RecommendationEffectivenessAnalyzer(
            _outcomeTracker!, _learningService!);
        _learningOutcomeService   = new LearningOutcomeService(
            _effectivenessAnalyzer, _interventionMemory!, _learningService!);
        _calibrationEngine        = new ConfidenceCalibrationEngine();
        _longitudinalTracker      = new LongitudinalBehaviorTracker();
        _governanceLearningPolicy = new GovernanceLearningPolicy(
            _settings!, _governanceDiagnostics!, _operationalState!);

        _logger.Info("Startup", "Phase 21 Adaptive Operational Learning & Pattern Intelligence initialized");

        // ── Operational Evidence & Workflow layer ─────────────────────────────
        // All services are stateless (no Start/Stop). Created after all upstream
        // intelligence and history services are running so they have data to work with.
        // The evidence graph is built lazily on first InvestigationPage navigation.
        _evidenceGraph      = new OperationalEvidenceGraph(
            _intelligence!,
            _operationHistory!,
            _session!,
            _anomalyDetection!,
            _driftDetection!,
            _earlyWarning!);
        _rootCauseEngine    = new RootCauseAnalysisEngine();
        _evidenceExplanation = new EvidenceExplanationService();
        _workflowEngine     = new OperationalWorkflowEngine();

        // ── Operational Companion layer ────────────────────────────────────────
        // Stateless/lightweight — session manager is in-memory only.
        // Conversation engine wraps existing services; no new background work.
        // Desktop context initialized here so it is non-null when passed to
        // OperationalConversationService — but capture is CONSENT-GATED:
        // window/clipboard polling only starts when the user has explicitly
        // enabled desktop context awareness (off by default). While stopped,
        // consumers see an empty snapshot.
        _desktopContext = new DesktopContextService(uiDispatcher);
        if (_settings!.Current.DesktopContextAwarenessEnabled)
        {
            _desktopContext.Start();
            _logger.Info("Startup", "Desktop context awareness active (user opt-in)");
        }
        else
        {
            _logger.Info("Startup",
                "Desktop context awareness off — window/clipboard context is not collected " +
                "(opt-in available in Settings)");
        }

        _companionSession = new CompanionSessionManager();

        // ── Local-only LLM enforcement ────────────────────────────────────────
        // Validate the endpoint from settings before handing it to LlmChatService.
        // OllamaClient also validates internally, but we log the violation here so
        // the governance audit trail captures it at startup rather than silently
        // recovering at the constructor level.
        var llmUrlValidation = LocalEndpointValidator.Validate(_settings!.Current.LlmEndpointUrl);
        if (!llmUrlValidation.IsAllowed)
            _logger.Warning("Startup",
                $"LLM endpoint '{llmUrlValidation.OriginalUrl}' is not local " +
                $"({llmUrlValidation.Classification}): {llmUrlValidation.Reason} " +
                $"Falling back to default localhost endpoint.");

        var llmBaseUrl = llmUrlValidation.IsAllowed
                             ? _settings!.Current.LlmEndpointUrl
                             : "http://localhost:11434";

        _llmChat = new LlmChatService(
            baseUrl:   llmBaseUrl,
            modelName: _settings!.Current.LlmModel,
            // The prompt builder aggregates a dozen services from this registry,
            // so it stays on the App side and is handed to the Core service as a
            // factory rather than being called statically from inside it.
            systemPromptFactory: LlmSystemContextBuilder.Build);

        // Home page chat — same endpoint and model, independent history.
        // See the HomeChat property for why the two surfaces are not sharing one.
        _homeChat = new LlmChatService(
            baseUrl:             llmBaseUrl,
            modelName:           _settings!.Current.LlmModel,
            systemPromptFactory: LlmSystemContextBuilder.Build);

        _chatSessions = new InMemoryChatSessionStore();

        var conversationSvc = new OperationalConversationService(
            _intelligence!,
            _narrative!,
            _timeline!,
            _evidenceGraph!,
            _rootCauseEngine!,
            _desktopContext!);
        _conversationService = conversationSvc;
        _conversationEngine  = conversationSvc;

        // ── Desktop Automation Orchestrator (Phase 17D) ───────────────────────
        // Created after timeline and execution engine so it can publish events
        // and delegate to registered executors. Stateless — no Start()/Stop().
        _automationOrchestrator = new AutomationOrchestrator(
            _executionEngine!,
            _timeline!,
            uiDispatcher);

        // ── Operational Trust layer (Phase 17E) ───────────────────────────────
        // Pure / stateless services first (no dependencies):
        _consentExplanation    = new ConsentExplanationService();
        _automationTransparency = new AutomationTransparencyEngine();

        // Audit service loads persisted entries from disk in its constructor and
        // prunes entries older than 30 days. Non-blocking — file read is fast.
        // Given the logger so persistence failures surface instead of being
        // silently dropped (the ledger is the accountability record).
        _automationAudit = new AutomationAuditService(_logger);

        // Consent service depends on timeline (for publishing consent events),
        // audit (for recording every decision), and the two stateless services.
        _automationConsent = new AutomationConsentService(
            _timeline!,
            _automationAudit,
            _consentExplanation,
            _automationTransparency,
            ui);

        // Trust manager subscribes to consent events to adapt the posture.
        // Created last so the audit ledger is populated before it begins listening.
        _trustManager = new OperationalTrustManager(
            _automationAudit,
            _automationConsent,
            _timeline!);

        // ── Phase 18C: Trust & Governance Hardening ──────────────────────────
        // Governance diagnostics is created earlier (Phase 19 observability
        // block) because the cognitive engine and learning policy inject it;
        // all trust checks below can publish audit events through it.

        // Consent integrity validation — verify the persisted consent posture
        // has not been tampered with outside the normal UI flows.
        _trustIntegrity          = new TrustIntegrityService();
        _consentIntegrityValidator = new ConsentIntegrityValidator(_trustIntegrity);
        _trustPostureRecovery    = new TrustPostureRecoveryManager(_consentIntegrityValidator);

        // Apply persisted operational trust settings (with tamper detection).
        var rawSettings = _settings.Current;
        var savedSettings = _trustPostureRecovery.ValidateOrRecover(rawSettings, out var tamperDetected);

        if (tamperDetected)
        {
            _logger.Warning("Startup",
                "Trust posture tamper detected — consent settings reset to safe defaults. " +
                "The integrity file did not match the current settings.json.");
            _governanceDiagnostics.RecordTamperDetected(
                "Consent posture in settings.json did not match the stored integrity signature. " +
                "Reset to AskForMediumAndHighRisk / ConfirmBeforeAction.");
        }

        // Publish endpoint rejection to governance log if URL was overridden above.
        if (!llmUrlValidation.IsAllowed)
            _governanceDiagnostics.RecordEndpointRejected(
                llmUrlValidation.OriginalUrl, llmUrlValidation.Reason);

        if (Enum.TryParse<TrustConsentMode>(savedSettings.ConsentMode, out var savedConsentMode))
            _automationConsent!.SetMode(savedConsentMode);
        if (Enum.TryParse<AutomationMode>(savedSettings.AutomationMode, out var savedAutoMode))
            _automationOrchestrator!.SetMode(savedAutoMode);

        // ── Autonomous Workflow Execution layer (Phase 17F) ───────────────────
        // Stateless/pure services first — no I/O, no background work.
        _safeExecutionValidator = new SafeExecutionValidator();
        _executionNarrative     = new ExecutionNarrativeService();
        _goalResolver           = new OperationalGoalResolver();

        // Checkpoint manager loads persisted state from disk and prunes stale entries.
        _checkpointManager = new WorkflowCheckpointManager();

        // Review gate dispatches approval events to the UI thread.
        _humanReviewGate = new HumanReviewGate(_timeline!, ui);

        // File-operation services depend on the safety validator.
        _fileDiscovery  = new OperationalFileDiscoveryService(_safeExecutionValidator);
        _fileOrgWorkflow = new FileOrganizationWorkflowService(_safeExecutionValidator);

        // Planner is pure — no dependencies on runtime services.
        _workflowPlanner = new WorkflowExecutionPlanner();

        // Coordinator wires together all step-level concerns. Given the logger
        // so best-effort (fire-and-forget) step failures are observed.
        _taskCoordinator = new TaskCompletionCoordinator(
            _safeExecutionValidator,
            _humanReviewGate,
            _checkpointManager,
            _executionNarrative,
            _fileOrgWorkflow,
            _fileDiscovery,
            _executionEngine!,
            _timeline!,
            _logger);

        // Top-level engine — created last so all dependencies are ready.
        _autonomousWorkflowEngine = new AutonomousWorkflowEngine(
            _goalResolver,
            _workflowPlanner,
            _taskCoordinator,
            _executionNarrative,
            _checkpointManager,
            _timeline!);

        // ── Semantic Desktop Understanding layer (Phase 18B) ─────────────────
        // ConsentBoundScreenAnalysis gates ALL visual analysis behind user approval.
        // VisualContextService is the top-level orchestrator; it is lazy (runs only
        // when explicitly invoked) and never polls in the background.
        _visualConsent       = new ConsentBoundScreenAnalysis(_timeline!, uiDispatcher);
        _screenCapture       = new ScreenCaptureCoordinator();
        _windowAnalyzer      = new WindowSemanticAnalyzer();
        _explorerInterpreter = new ExplorerVisualInterpreter();
        _settingsInterpreter = new SettingsPageInterpreter();
        _workflowLocator     = new VisualWorkflowLocator();
        _visualContext       = new VisualContextService(
            _visualConsent,
            _screenCapture,
            _windowAnalyzer,
            _explorerInterpreter,
            _settingsInterpreter,
            _workflowLocator,
            _timeline!,
            uiDispatcher);

        // ── Hourly telemetry retention (governed) ─────────────────────────────
        // Downsampling + stale-row eviction now runs through
        // TelemetryRetentionService under the TelemetryRetention slot (IdleOnly)
        // instead of an ungoverned anonymous timer (adoption audit site #4).
        _telemetryRetention = new TelemetryRetentionService(
            _telHistoryRepo!, _governance, _operationalLogger!);
        _telemetryRetention.Start();

        // ── Health analytics pre-warm ─────────────────────────────────────────
        // Compute and cache the machine health report in the background at startup
        // so LlmSystemContextBuilder can inject 7-day averages from the first
        // chat message — no Dashboard navigation required.
        // Fire-and-forget; returns an empty report on a new install, which is fine.
        _ = Task.Run(async () =>
        {
            try { await _healthTrajectory!.ComputeReportAsync().ConfigureAwait(false); }
            catch { /* best-effort pre-warm — analytics failure is non-critical */ }
        });

        // ── Composition registry (strangler seam, ROADMAP C4) ────────────────
        // One explicit factory per ViewModel whose page has been migrated off
        // the static locator. Factories close over the services built above,
        // so construction order stays hand-ordered and visible. Migrate one
        // page per session: add its factory here, switch the page to
        // App.Registry.Resolve<T>(), then tighten the debt-ratchet baseline.
        _registry = new ServiceRegistry();
        _registry.Register(() => new ViewModels.TimelinePageViewModel(_timeline!));
    }

    /// <summary>
    /// Stops and disposes all services.
    /// Call from the main window's Closed event handler.
    /// </summary>
    public static void Shutdown()
    {
        _logger?.Info("Startup", "AppServices.Shutdown() called — tearing down services");
        _settings = null;

        // Composition registry holds only factories over services torn down
        // below — clear it first so nothing resolves during shutdown.
        _registry = null;

        // Simulation, history, verification, and trajectory services are stateless — null them.
        _simulationEngine    = null;
        _simulationHistory   = null;
        _outcomeVerification = null;
        _healthTrajectory    = null;

        // Phase 18C: Trust & Governance Hardening — stateless services.
        _governanceDiagnostics   = null;
        _trustPostureRecovery    = null;
        _consentIntegrityValidator = null;
        _trustIntegrity          = null;
        _persistenceMonitor      = null;

        // Phase 19: Cognitive Reasoning Layer — stateless, just null references.
        // ReasoningMemoryService implements IDisposable (ReaderWriterLockSlim).
        (_reasoningMemory as IDisposable)?.Dispose();
        _cognitiveReasoning   = null;
        _arbitrator           = null;
        _reasoningMemory      = null;
        _cognitiveMetrics     = null;
        _reasoningDiagnostics = null;
        _outcomeTracker       = null;
        _patternAnalyzer      = null;

        // Phase 21: Adaptive Learning Layer — null references only.
        _patternIntelligence      = null;
        _adaptiveBaselines        = null;
        _effectivenessAnalyzer    = null;
        _learningOutcomeService   = null;
        _calibrationEngine        = null;
        _longitudinalTracker      = null;
        _governanceLearningPolicy = null;
        _adaptiveLearningMetrics  = null;
        _learningDiagnostics      = null;

        // Phase 20: Interaction Layer — stateless services, just null references.
        _cognitivePresentationService = null;
        _densityCoordinator           = null;
        _unifiedRecommendationService = null;
        _explainabilityRenderer       = null;
        _cognitiveTimelineBuilder     = null;
        _attentionCoordinator         = null;
        _interactionMetrics           = null;
        _interactionDiagnostics       = null;
        _contextSynthesizer = null;

        // Personalization layer — stateless services, just null references.
        _interventionMemory        = null;
        _personalizationEngine     = null;
        _userBehaviorClassifier    = null;
        _alertFatigueManager       = null;
        _recommendationExplanation = null;

        // Evidence & Workflow layer — stateless, just null references.
        _evidenceGraph      = null;
        _rootCauseEngine    = null;
        _evidenceExplanation = null;
        _workflowEngine     = null;

        // Visual context layer — no background work; just null all references.
        _visualContext?.Dispose();
        _visualContext       = null;
        _workflowLocator     = null;
        _settingsInterpreter = null;
        _explorerInterpreter = null;
        _windowAnalyzer      = null;
        _screenCapture       = null;
        _visualConsent       = null;

        // Autonomous workflow layer — cancel any running workflow, then null references.
        _autonomousWorkflowEngine?.CancelCurrentWorkflow();
        _autonomousWorkflowEngine = null;
        _taskCoordinator          = null;
        _workflowPlanner          = null;
        _fileOrgWorkflow          = null;
        _fileDiscovery            = null;
        _humanReviewGate          = null;
        _checkpointManager        = null;
        _goalResolver             = null;
        _executionNarrative       = null;
        _safeExecutionValidator   = null;

        // Trust layer — stateless services, just null references.
        // AutomationAuditService persists to disk asynchronously; no explicit flush needed.
        _trustManager           = null;
        _automationConsent      = null;
        // AutomationAuditService implements IDisposable (write gate +
        // ReaderWriterLockSlim); Dispose waits briefly for an in-flight
        // ledger write so the last audit entries reach disk.
        _automationAudit?.Dispose();
        _automationAudit        = null;
        _consentExplanation     = null;
        _automationTransparency = null;

        // Companion layer — dispose LlmChatService (owns HttpClient instances), null the rest.
        (_llmChat as IDisposable)?.Dispose();
        _llmChat                = null;
        (_homeChat as IDisposable)?.Dispose();
        _homeChat               = null;
        _chatSessions           = null;
        _companionSession       = null;
        _conversationEngine     = null;
        _conversationService    = null;
        _automationOrchestrator = null;

        // Desktop context layer — stop polling and dispose STA thread.
        _desktopContext?.Stop();
        _desktopContext = null;

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

        // Stop rollback maintenance before governance — each sweep acquires a
        // governance slot, so it must wind down while governance is still live.
        _rollbackMaintenance?.Stop();
        _rollbackMaintenance = null;

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

        // Stop the telemetry retention loop before disposing SQLite.
        _telemetryRetention?.Stop();
        _telemetryRetention = null;

        // Replay service is stateless — just null the reference.
        _replayService = null;

        // ExplainEngine must stop before timeline and narrative —
        // it holds subscriptions to both.
        _explainEngine?.Stop();
        _explainEngine = null;

        // ProcessIntelligence must stop before timeline and telemetry —
        // it holds subscriptions to both.
        _processIntelligence?.Stop();
        _processIntelligence = null;

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

        // ── Phase 18: Unified Operational Core teardown ───────────────────────
        // Tear down in reverse init order: metrics → state → lifecycle → event bus.
        // OperationalLogger and logger are disposed last.
        (_platformMetrics     as IDisposable)?.Dispose(); _platformMetrics      = null;
        (_operationalState    as IDisposable)?.Dispose(); _operationalState     = null;
        _lifecycleCoordinator?.Dispose();                 _lifecycleCoordinator = null;
        _healthRegistry?.Dispose();                       _healthRegistry       = null;
        (_eventBus            as IDisposable)?.Dispose(); _eventBus             = null;
        (_operationalLogger   as IDisposable)?.Dispose(); _operationalLogger    = null;

        // Dispose the logger last so shutdown log entries are written.
        if (_logger is IDisposable disposableLogger)
        {
            disposableLogger.Dispose();
            _logger = null;
        }
    }
}

