using Lucid.Core.Infrastructure;

namespace Lucid.Services.Infrastructure.Lifecycle;

/// <summary>
/// Orchestrates the startup and shutdown of all registered
/// <see cref="IServiceLifecycleParticipant"/> services in the Lucid platform.
///
/// Startup model:
///   Services are initialized in ascending <see cref="StartupPhase"/> order.
///   Within each phase, services run sequentially in registration order.
///   Cancellation propagates to all pending initializations.
///
/// Shutdown model:
///   Services are shut down in ascending <see cref="ShutdownPhase"/> order
///   (equivalent to reverse startup order). Each service is given a
///   bounded timeout before it is abandoned.
///
/// Failure model:
///   A failed initialization is recorded in <see cref="ServiceHealthRegistry"/>
///   but does NOT prevent subsequent services from starting. The coordinator
///   continues so the platform can reach a partially-healthy state and report
///   clearly rather than failing silently.
///
/// Integration:
///   Existing AppServices initialization flow is not replaced by this coordinator.
///   New services opt in by implementing <see cref="IServiceLifecycleParticipant"/>
///   and registering via <see cref="Register"/>. Adoption is incremental.
/// </summary>
public sealed class LifecycleCoordinator : IDisposable
{
    private readonly List<IServiceLifecycleParticipant> _participants  = [];
    private readonly ServiceHealthRegistry              _healthRegistry;
    private readonly ILucidLogger?                      _logger;
    private readonly TimeSpan                           _shutdownTimeout;
    private          bool                               _disposed;

    private const string Category = "Lifecycle";

    /// <summary>
    /// Provides a read-only view of platform service health.
    /// </summary>
    public ServiceHealthRegistry Health => _healthRegistry;

    /// <param name="healthRegistry">Shared health registry for reporting.</param>
    /// <param name="logger">Optional logger — coordinator runs silently without one.</param>
    /// <param name="shutdownTimeout">
    /// Per-service shutdown timeout. Services that exceed this are abandoned.
    /// Defaults to 10 seconds.
    /// </param>
    public LifecycleCoordinator(
        ServiceHealthRegistry healthRegistry,
        ILucidLogger?         logger          = null,
        TimeSpan?             shutdownTimeout = null)
    {
        _healthRegistry  = healthRegistry;
        _logger          = logger;
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(10);
    }

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a service participant. Must be called before
    /// <see cref="StartupAsync"/>.
    /// </summary>
    public void Register(IServiceLifecycleParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        _participants.Add(participant);
        _healthRegistry.Register(participant);
    }

    // ── Startup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes all registered participants in <see cref="StartupPhase"/> order.
    /// Returns when all phases have completed (or been abandoned on cancellation).
    /// </summary>
    public async Task StartupAsync(CancellationToken cancellationToken = default)
    {
        _logger?.Info(Category, "Platform startup sequence beginning");

        var phases = Enum.GetValues<StartupPhase>().OrderBy(p => (int)p);

        foreach (var phase in phases)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger?.Warning(Category, $"Startup cancelled during phase {phase}");
                break;
            }

            var phaseParticipants = _participants
                .Where(p => p.StartupPhase == phase)
                .ToList();

            if (phaseParticipants.Count == 0) continue;

            _logger?.Debug(Category, $"Starting phase {phase} ({phaseParticipants.Count} services)");

            foreach (var participant in phaseParticipants)
            {
                if (cancellationToken.IsCancellationRequested) break;

                _healthRegistry.Report(participant.ServiceName, ServiceHealthState.Initializing);

                try
                {
                    await participant.InitializeAsync(cancellationToken).ConfigureAwait(false);

                    var health = participant.GetHealthState();
                    _healthRegistry.Report(participant.ServiceName, health);

                    _logger?.Debug(Category,
                        $"  ✓ {participant.ServiceName} ({phase}) — {health}");
                }
                catch (OperationCanceledException)
                {
                    _healthRegistry.Report(participant.ServiceName, ServiceHealthState.Stopped,
                        "Initialization cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    _healthRegistry.Report(participant.ServiceName, ServiceHealthState.Failed,
                        ex.Message);

                    _logger?.Error(Category,
                        $"  ✗ {participant.ServiceName} ({phase}) initialization failed — continuing",
                        ex);
                    // Non-fatal: continue so other services can start.
                }
            }
        }

        _logger?.Info(Category,
            $"Platform startup complete — " +
            $"{_participants.Count(p => _healthRegistry.GetEntry(p.ServiceName)?.State == ServiceHealthState.Healthy)} healthy, " +
            $"{_healthRegistry.DegradedCount()} degraded, " +
            $"{_participants.Count(p => _healthRegistry.GetEntry(p.ServiceName)?.State == ServiceHealthState.Failed)} failed");
    }

    // ── Shutdown ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Shuts down all registered participants in <see cref="ShutdownPhase"/> order.
    /// Each service is given <see cref="_shutdownTimeout"/> before being abandoned.
    /// </summary>
    public async Task ShutdownAsync()
    {
        _logger?.Info(Category, "Platform shutdown sequence beginning");

        var phases = Enum.GetValues<ShutdownPhase>().OrderBy(p => (int)p);

        foreach (var phase in phases)
        {
            var phaseParticipants = _participants
                .Where(p => p.ShutdownPhase == phase)
                .ToList();

            if (phaseParticipants.Count == 0) continue;

            _logger?.Debug(Category, $"Shutting down phase {phase} ({phaseParticipants.Count} services)");

            foreach (var participant in phaseParticipants)
            {
                using var cts = new CancellationTokenSource(_shutdownTimeout);
                try
                {
                    await participant.ShutdownAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                    _healthRegistry.Report(participant.ServiceName, ServiceHealthState.Stopped);
                    _logger?.Debug(Category, $"  ✓ {participant.ServiceName} stopped");
                }
                catch (OperationCanceledException)
                {
                    _healthRegistry.Report(participant.ServiceName, ServiceHealthState.Stopped,
                        $"Shutdown timed out after {_shutdownTimeout.TotalSeconds}s");
                    _logger?.Warning(Category,
                        $"  ⚠ {participant.ServiceName} shutdown timed out — abandoned");
                }
                catch (Exception ex)
                {
                    _healthRegistry.Report(participant.ServiceName, ServiceHealthState.Stopped,
                        $"Shutdown error: {ex.Message}");
                    _logger?.Error(Category,
                        $"  ✗ {participant.ServiceName} shutdown threw — abandoned", ex);
                }
            }
        }

        _logger?.Info(Category, "Platform shutdown sequence complete");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _participants.Clear();
    }
}
