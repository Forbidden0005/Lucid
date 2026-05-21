namespace ExplainMyPC.Services.Persistence;

// ── Telemetry sample resolution tiers ─────────────────────────────────────────

/// <summary>
/// How finely a telemetry row was sampled or aggregated.
/// Lower values are finer-grained; higher values cover longer windows.
///
/// Retention policy (enforced by <see cref="HistoricalTelemetryRepository"/>):
///   Raw30s   — kept for 48 hours   (~5,760 rows/day)
///   Min1     — kept for 14 days    (~1,440 rows/day)
///   Min15    — kept for 90 days    (~96 rows/day)
///   Hour1    — kept for 2 years    (~24 rows/day)
/// </summary>
public enum TelemetrySampleResolution
{
    /// <summary>One row per 30-second write window — full resolution.</summary>
    Raw30s = 0,

    /// <summary>1-minute aggregate (mean of raw rows in the minute).</summary>
    Min1   = 1,

    /// <summary>15-minute aggregate.</summary>
    Min15  = 2,

    /// <summary>Hourly aggregate.</summary>
    Hour1  = 3,
}

// ── Persisted telemetry sample ─────────────────────────────────────────────────

/// <summary>
/// A single telemetry row as read from or written to the SQLite database.
/// Covers all five hardware metrics; GPU and temperature may be null when
/// the sensor was absent or unreliable at sample time.
/// </summary>
public sealed record PersistedTelemetrySample
{
    public long                      Id          { get; init; }
    public DateTimeOffset            SampledAt   { get; init; }
    public TelemetrySampleResolution Resolution  { get; init; }
    public double                    CpuPercent  { get; init; }
    public double                    RamPercent  { get; init; }
    public double?                   GpuPercent  { get; init; }
    public double                    DiskPercent { get; init; }
    public double?                   TempCelsius { get; init; }
    /// <summary>Number of raw samples aggregated into this row (1 for Raw30s).</summary>
    public int                       SampleCount { get; init; } = 1;
}

// ── Persisted insight history row ──────────────────────────────────────────────

/// <summary>
/// A single insight onset/resolution pair as stored in the database.
/// <see cref="ResolvedAt"/> is null for insights that were still active when last written.
/// </summary>
public sealed record PersistedInsightRecord
{
    public long           Id              { get; init; }
    public string         InsightId       { get; init; } = string.Empty;
    public string         Title           { get; init; } = string.Empty;
    public int            SeverityOrdinal { get; init; }
    public DateTimeOffset OnsetAt         { get; init; }
    public DateTimeOffset? ResolvedAt     { get; init; }
    public int?           DurationSeconds { get; init; }
}

// ── Persisted timeline event ───────────────────────────────────────────────────

/// <summary>
/// A timeline event as stored in the database.
/// One-to-one with <see cref="Timeline.TimelineEvent"/> but uses only primitive types
/// so System.Text.Json or SQLite can round-trip it without custom converters.
/// </summary>
public sealed record PersistedTimelineEvent
{
    public string  EventId             { get; init; } = string.Empty;
    public int     EventTypeOrdinal    { get; init; }
    public int     SeverityOrdinal     { get; init; }
    public string  Title               { get; init; } = string.Empty;
    public string? Detail              { get; init; }
    public DateTimeOffset OccurredAt   { get; init; }
    public string? RelatedInsightId    { get; init; }
    public string? AttributedProcesses { get; init; }  // comma-joined list
    public string? ActionId            { get; init; }
}
