using ExplainMyPC.Models;

namespace ExplainMyPC.Intelligence.Models;

/// <summary>
/// The engine's input: everything it knows about the system at a point in time.
///
/// RecentHistory allows trend-aware rules to detect sustained conditions
/// (e.g., CPU has been above 80% for the last 10 seconds) rather than reacting
/// to momentary spikes. Ordered oldest-first; Current is the newest reading.
///
/// StartupAppCount is provided by the StartupScanner (Phase 2). Pass 0 if
/// the scanner has not run yet — startup rules will silently no-op.
/// </summary>
public sealed record SystemSnapshot(
    TelemetryReading                Current,
    IReadOnlyList<TelemetryReading> RecentHistory,
    int                             StartupAppCount);
