using System.Collections.Concurrent;

namespace Lucid.Services.Infrastructure.Lifecycle;

/// <summary>
/// Tracks the health state of all registered lifecycle participants.
///
/// Services report their health via <see cref="IServiceLifecycleParticipant.GetHealthState"/>.
/// The registry polls registered services and maintains an aggregate platform health view.
///
/// Thread-safe: all operations are safe for concurrent access.
/// </summary>
public sealed class ServiceHealthRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, ServiceHealthEntry> _entries = new();

    // ── Health state types ────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot of a service's reported health at a point in time.
    /// </summary>
    public sealed record ServiceHealthEntry(
        string           ServiceName,
        ServiceHealthState State,
        string?          Detail,
        DateTime         LastUpdated);

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a participant so its health state is tracked.
    /// Safe to call multiple times — subsequent calls are idempotent.
    /// </summary>
    public void Register(IServiceLifecycleParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        _entries.TryAdd(participant.ServiceName,
            new ServiceHealthEntry(
                participant.ServiceName,
                ServiceHealthState.Initializing,
                Detail: null,
                DateTime.UtcNow));
    }

    // ── Reporting ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the stored health entry for the given service.
    /// Called by the coordinator after each initialization/shutdown step
    /// and by polling cycles.
    /// </summary>
    public void Report(
        string             serviceName,
        ServiceHealthState state,
        string?            detail = null)
    {
        _entries[serviceName] = new ServiceHealthEntry(
            serviceName, state, detail, DateTime.UtcNow);
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all registered health entries as an immutable snapshot.
    /// </summary>
    public IReadOnlyList<ServiceHealthEntry> GetAllEntries() =>
        [.. _entries.Values];

    /// <summary>
    /// Returns the current health entry for the named service, or null if unknown.
    /// </summary>
    public ServiceHealthEntry? GetEntry(string serviceName) =>
        _entries.TryGetValue(serviceName, out var entry) ? entry : null;

    /// <summary>
    /// Returns true if all registered services report <see cref="ServiceHealthState.Healthy"/>.
    /// </summary>
    public bool IsFullyHealthy() =>
        _entries.Values.All(e => e.State == ServiceHealthState.Healthy);

    /// <summary>
    /// Returns true if any registered service reports <see cref="ServiceHealthState.Failed"/>.
    /// </summary>
    public bool HasFailures() =>
        _entries.Values.Any(e => e.State == ServiceHealthState.Failed);

    /// <summary>
    /// Returns the count of services currently in degraded state.
    /// </summary>
    public int DegradedCount() =>
        _entries.Values.Count(e => e.State == ServiceHealthState.Degraded);

    /// <inheritdoc />
    public void Dispose() => _entries.Clear();
}

/// <summary>
/// Possible health states for a registered lifecycle participant.
/// </summary>
public enum ServiceHealthState
{
    /// <summary>Service is in the process of starting up.</summary>
    Initializing,

    /// <summary>Service is running normally.</summary>
    Healthy,

    /// <summary>Service is running but with reduced capability or under stress.</summary>
    Degraded,

    /// <summary>Service has failed and is not functional.</summary>
    Failed,

    /// <summary>Service has completed its shutdown sequence.</summary>
    Stopped,
}
