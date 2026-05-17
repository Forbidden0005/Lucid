using ExplainMyPC.Services;
using ExplainMyPC.Services.Execution;
using ExplainMyPC.Services.Execution.Executors;
using ExplainMyPC.Services.Intelligence;
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
    private static ITelemetryService?       _telemetry;
    private static ITelemetryHistoryBuffer? _history;
    private static ISystemInsightEngine?    _intelligence;
    private static ActionExecutorRegistry?  _executorRegistry;
    private static IActionExecutionEngine?  _executionEngine;

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
    /// Rule-based intelligence engine that evaluates heuristics on every
    /// telemetry tick and publishes findings when the active set changes.
    /// </summary>
    public static ISystemInsightEngine Intelligence =>
        _intelligence ?? throw new InvalidOperationException(
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

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and starts all application services.
    /// Must be called on the UI thread so services can capture the
    /// DispatcherQueue used to marshal readings back to the UI.
    /// </summary>
    public static void Initialize(DispatcherQueue uiDispatcher)
    {
        _telemetry    = new WindowsTelemetryService(uiDispatcher);
        _history      = new TelemetryHistoryBuffer();
        _intelligence = new SystemInsightEngine(_telemetry, _history);

        // Every snapshot flows into the history buffer automatically.
        // ReadingAvailable fires on the UI thread, so Record() is always
        // called from a single thread — the write lock is still held for
        // correctness when background analysis threads read concurrently.
        _telemetry.ReadingAvailable += (_, snapshot) => _history.Record(snapshot);

        _telemetry.Start();
        _intelligence.Start();

        // ── Remediation execution engine ──────────────────────────────────────
        // Phase 1 — safe open-application executors (no system modification).
        // Add new executors here as capabilities are implemented.
        _executorRegistry = new ActionExecutorRegistry();
        _executorRegistry.RegisterAll([
            // Phase 1 — guided navigation: open the relevant system tool and let
            // the user act. Completely safe, no system modification.
            new OpenTaskManagerExecutor(),      // action.cpu.open-task-manager
            new OpenStorageSenseExecutor(),     // action.disk.run-storage-sense
            new OpenStartupAppsExecutor(),      // action.startup.open-startup-apps
            new OpenWindowsSecurityExecutor(),  // action.security.open-windows-security

            // Phase 2 — real cleanup: scans known-safe temp directories and moves
            // stale files to a rollback staging area (atomic rename on same drive).
            // Supports dry-run preview, per-file logging, cancellation, and rollback.
            new TempFileCleanupExecutor(),      // action.disk.clean-temp-files

            // Phase 3 (planned): startup management — enumerate + toggle startup entries
            // Phase 4 (planned): repair commands — SFC /scannow, DISM, network reset
        ]);
        _executionEngine  = new ActionExecutionEngine(_executorRegistry);

        // Future services registered here, e.g.:
        // _storage  = new WindowsStorageService();
        // _process  = new WindowsProcessService();
        // _security = new WindowsSecurityService();
    }

    /// <summary>
    /// Stops and disposes all services.
    /// Call from the main window's Closed event handler.
    /// </summary>
    public static void Shutdown()
    {
        _executionEngine  = null;
        _executorRegistry = null;

        _intelligence?.Stop();
        if (_intelligence is IDisposable id) id.Dispose();
        _intelligence = null;

        _telemetry?.Stop();
        if (_telemetry is IDisposable td) td.Dispose();
        if (_history  is IDisposable hd) hd.Dispose();
        _telemetry = null;
        _history   = null;
    }
}
