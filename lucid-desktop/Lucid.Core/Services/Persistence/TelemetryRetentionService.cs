using Lucid.Services.Diagnostics.Logging;
using Lucid.Services.Governance;

namespace Lucid.Services.Persistence;

/// <summary>
/// Governed hourly telemetry retention: downsamples raw telemetry rows into
/// coarser buckets and evicts stale rows via
/// <see cref="HistoricalTelemetryRepository.DownsampleAndPurgeAsync"/>.
///
/// Replaces the anonymous hourly timer that lived in AppServices (governance
/// adoption audit 2026-07-25, site #4) — that lambda ran with no governance
/// contact and silently skipped the pass forever if refused (it never was,
/// because it never asked).
///
/// Resource governance:
///   Each pass runs under the <see cref="WorkloadCategory.TelemetryRetention"/>
///   slot (IdleOnly, limit 1) via <see cref="GovernedWorkRunner.RunOrDeferAsync"/>.
///   When the system is busy the pass is deferred for retry on the next queue
///   drain instead of dropped; the hourly tick plus deferred-entry dedupe keep
///   the queue from stacking duplicates. SQLite WAL means the pass never blocks
///   the main thread; all work runs on the thread pool.
///
/// Failures are logged, never unobserved — a silently-dead retention sweep
/// would let telemetry rows accumulate unbounded over long uptime.
/// </summary>
public sealed class TelemetryRetentionService
{
    private const string LogCategory  = "Persistence";
    private const string WorkloadName = "Telemetry downsample & purge";

    /// <summary>Interval between retention passes (also the initial delay).</summary>
    private static readonly TimeSpan PassInterval = TimeSpan.FromHours(1);

    private readonly HistoricalTelemetryRepository _repository;
    private readonly IRuntimeGovernanceService     _governance;
    private readonly IOperationalLogger            _logger;

    private CancellationTokenSource? _cts;

    public TelemetryRetentionService(
        HistoricalTelemetryRepository repository,
        IRuntimeGovernanceService     governance,
        IOperationalLogger            logger)
    {
        _repository = repository;
        _governance = governance;
        _logger     = logger;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    /// <summary>Starts the hourly retention loop. Idempotent.</summary>
    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    /// <summary>Stops the retention loop. Safe to call when not started.</summary>
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
            using var timer = new PeriodicTimer(PassInterval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await RunOnceAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            // Housekeeping must never crash the app.
            _logger.Error(LogCategory, "Telemetry retention loop stopped unexpectedly.", exception: ex);
        }
    }

    // ── Single pass ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs one governed retention pass. Returns <c>false</c> when governance
    /// deferred the pass (it will retry from the queue). Public so diagnostics
    /// tooling can trigger a pass on demand.
    /// </summary>
    public Task<bool> RunOnceAsync()
        => GovernedWorkRunner.RunOrDeferAsync(
            _governance,
            WorkloadCategory.TelemetryRetention,
            WorkloadName,
            work: async () =>
            {
                try
                {
                    await _repository.DownsampleAndPurgeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning(LogCategory,
                        "Telemetry downsample/purge failed — retention will retry next hour.",
                        exception: ex);
                }
            },
            onDeferred: reason => _logger.Debug(LogCategory,
                $"Telemetry retention deferred by governance: {reason}"));
}
