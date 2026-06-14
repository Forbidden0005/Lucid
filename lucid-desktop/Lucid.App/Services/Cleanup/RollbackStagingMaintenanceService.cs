using Lucid.Services.Diagnostics.Logging;
using Lucid.Services.Governance;
using Lucid.Services.Settings;

namespace Lucid.Services.Cleanup;

/// <summary>
/// Background housekeeping for the rollback staging area
/// (<c>%LOCALAPPDATA%\Lucid\Rollback</c>).
///
/// Reversible cleanup executors move files into per-run staging sets instead of
/// deleting them, so an action can be undone. Nothing previously reclaimed those
/// sets, so the staging tree grew without bound and the bytes a cleanup claimed
/// to free were merely relocated. This service enforces the retention window
/// (<see cref="AppSettings.RollbackRetentionDays"/>) so expired sets are actually
/// removed and the space is genuinely returned to the user.
///
/// Resource governance:
///   The sweep is classified <see cref="WorkloadCategory.RollbackMaintenance"/>
///   (IdleOnly). Each pass acquires a governance slot first and is skipped when
///   the runtime is not calm, honouring the doctrine that maintenance must never
///   compete with foreground work or run during gaming / thermal / low-power
///   modes. All filesystem work runs on the thread pool.
///
/// Schedule:
///   One sweep shortly after startup (once the boot burst has settled), then one
///   every <see cref="SweepInterval"/>. <see cref="Stop"/> cancels the loop.
///
/// Privacy:
///   Only aggregate counts and reclaimed-byte totals are logged — never file
///   paths or names — per the <see cref="IOperationalLogger"/> contract.
/// </summary>
public sealed class RollbackStagingMaintenanceService
{
    private const string LogCategory = "Cleanup";
    private const string WorkloadName = "Rollback staging maintenance";

    /// <summary>Delay before the first sweep, to stay clear of the startup burst.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    /// <summary>Interval between subsequent sweeps.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);

    private readonly ISettingsService          _settings;
    private readonly IRuntimeGovernanceService _governance;
    private readonly IOperationalLogger        _logger;
    private readonly string                    _rollbackRoot;

    private CancellationTokenSource? _cts;

    /// <param name="rollbackRoot">
    /// Override for the staging root — defaults to
    /// <see cref="RollbackStagingSweeper.DefaultRollbackRoot"/>. Primarily a test seam.
    /// </param>
    public RollbackStagingMaintenanceService(
        ISettingsService          settings,
        IRuntimeGovernanceService governance,
        IOperationalLogger        logger,
        string?                   rollbackRoot = null)
    {
        _settings     = settings;
        _governance   = governance;
        _logger       = logger;
        _rollbackRoot = rollbackRoot ?? RollbackStagingSweeper.DefaultRollbackRoot();
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    /// <summary>Starts the periodic sweep loop. Idempotent.</summary>
    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    /// <summary>Stops the sweep loop. Safe to call when not started.</summary>
    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
    }

    // ── Loop ────────────────────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
            await SweepOnceAsync(ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(SweepInterval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await SweepOnceAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            // Housekeeping must never crash the app.
            _logger.Error(LogCategory, "Rollback maintenance loop stopped unexpectedly.", exception: ex);
        }
    }

    // ── Single pass ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs one governed sweep. Returns <see cref="RollbackSweepResult.Empty"/>
    /// when the governance layer defers the work. Public so diagnostics tooling
    /// can trigger a sweep on demand.
    /// </summary>
    public async Task<RollbackSweepResult> SweepOnceAsync(CancellationToken ct = default)
    {
        if (!_governance.TryAcquireSlot(
                WorkloadCategory.RollbackMaintenance, WorkloadName, out var refusal))
        {
            _logger.Debug(LogCategory,
                $"Rollback maintenance deferred by governance: {refusal ?? "system busy"}.");
            return RollbackSweepResult.Empty;
        }

        try
        {
            // Clamp to a sane floor so a misconfigured value can never purge
            // staging sets that are only minutes old.
            int days   = Math.Max(1, _settings.Current.RollbackRetentionDays);
            var maxAge = TimeSpan.FromDays(days);
            var now    = DateTimeOffset.UtcNow;

            var result = await Task
                .Run(() => RollbackStagingSweeper.Sweep(_rollbackRoot, maxAge, now), ct)
                .ConfigureAwait(false);

            if (result.StagingSetsRemoved > 0)
            {
                _logger.Info(LogCategory,
                    $"Rollback maintenance removed {result.StagingSetsRemoved} expired staging " +
                    $"set(s) (> {days}d), reclaiming {result.BytesReclaimed:N0} byte(s).");
            }
            else if (result.Errors > 0)
            {
                _logger.Warning(LogCategory,
                    $"Rollback maintenance completed with {result.Errors} unreadable staging set(s).");
            }

            return result;
        }
        finally
        {
            _governance.ReleaseSlot(WorkloadCategory.RollbackMaintenance, WorkloadName);
        }
    }
}
