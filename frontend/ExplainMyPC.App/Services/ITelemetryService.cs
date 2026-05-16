using ExplainMyPC.Models;

namespace ExplainMyPC.Services;

/// <summary>
/// Produces periodic system telemetry readings.
///
/// Phase 1: implemented by MockTelemetryService (random data).
/// Phase 2: implemented by a Rust FFI bridge that calls real Windows APIs.
///
/// Polling contract (per architecture.md):
///   CPU / RAM / GPU  →  ReadingUpdated fires ~every 1 second
///   Disk I/O          →  included in the same reading but at ~2s fidelity
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Raised on a background thread when a new telemetry snapshot is ready.
    /// Subscribers MUST marshal to the UI thread before touching WinUI objects.
    /// </summary>
    event EventHandler<TelemetryReading> ReadingUpdated;

    /// <summary>Start emitting readings.</summary>
    void Start();

    /// <summary>Stop emitting readings without disposing the service.</summary>
    void Stop();
}
