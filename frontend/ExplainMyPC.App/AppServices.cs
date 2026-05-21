using ExplainMyPC.Services;
using ExplainMyPC.Services.Baseline;
using ExplainMyPC.Services.Learning;
using ExplainMyPC.Services.Execution;
using ExplainMyPC.Services.Execution.Executors;
using ExplainMyPC.Services.Explain;
using ExplainMyPC.Services.History;
using ExplainMyPC.Services.Intelligence;
using ExplainMyPC.Services.Narrative;
using ExplainMyPC.Services.Replay;
using ExplainMyPC.Services.Security;
using ExplainMyPC.Services.Session;
using ExplainMyPC.Services.Startup;
using ExplainMyPC.Services.Storage;
using ExplainMyPC.Services.Timeline;
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

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and starts all application services.
    /// Must be called on the UI thread so services can capture the
    /// DispatcherQueue used to marshal readings back to the UI.
    /// </summary>
    public static void Initialize(DispatcherQueue uiDispatcher)
    {
        _telemetry = new WindowsTelemetryService(uiDispatcher);
        _history   = new TelemetryHistoryBuffer();

        // Adaptive baseline service — learns this machine's normal operating
        // ranges over time and persists them to %LOCALAPPDATA%\ExplainMyPC\.
        // Must be created before SystemInsightEngine so the baseline rules
        // receive a valid service reference at construction time.
        _baseline = new SystemBaselineService(_telemetry);

        _intelligence = new SystemInsightEngine(_telemetry, _history, _baseline);

        // Every snapshot flows into the history buffer automatically.
        // ReadingAvailable fires on the UI thread, so Record() is always
        // called from a single thread — the write lock is still held for
        // correctness when background analysis threads read concurrently.
        _telemetry.ReadingAvailable += (_, snapshot) => _history.Record(snapshot);

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
        _executionEngine  = new ActionExecutionEngine(_executorRegistry);

        // ── Operational Timeline ──────────────────────────────────────────────
        // Aggregates events from intelligence, session, narrative, and history.
        // Created after all of its upstream services are running.
        // Passes the DispatcherQueue so history load results can be marshalled
        // back to the UI thread from the thread-pool read.
        _timeline = new TimelineAggregationService(
            _intelligence, _session, _narrative, _operationHistory, uiDispatcher);
        _timeline.Start(); // must start after narrative (subscribes to NarrativeUpdated)

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
        var learningSvc = new RemediationLearningService(_operationHistory, _replayService);
        _ = learningSvc.LoadPersistedProfilesAsync();
        _ = learningSvc.AnalyzePendingActionsAsync();
        _learningService = learningSvc;
    }

    /// <summary>
    /// Stops and disposes all services.
    /// Call from the main window's Closed event handler.
    /// </summary>
    public static void Shutdown()
    {
        _executionEngine    = null;
        _executorRegistry   = null;
        _operationHistory   = null;
        _startupManagement  = null;

        // Learning service is stateless after init — just null the reference.
        _learningService = null;

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
    }
}
