using System.Text.Json;
using System.Text.Json.Serialization;
using ExplainMyPC.Helpers;
using ExplainMyPC.Services.Startup;

namespace ExplainMyPC.Services.Baseline;

/// <summary>
/// Production implementation of <see cref="ISystemBaselineService"/>.
///
/// Subscribes to the live telemetry feed and incrementally updates four
/// <see cref="WelfordStats"/> accumulators — one per monitored dimension.
/// On every tick a fresh <see cref="MachineBaseline"/> snapshot is lazily
/// built from the accumulator state and cached until the next update.
///
/// Threading:
///   All accumulator updates happen in <see cref="OnReadingAvailable"/>,
///   which fires on the UI thread (marshalled by WindowsTelemetryService).
///   <see cref="CurrentBaseline"/> is also read on the UI thread by rules.
///   No locking is needed for the accumulators themselves.
///
///   Persistence is written on a background Task to avoid stalling the UI.
///   The serialized snapshot is captured on the UI thread before handing off
///   to the background task — the background thread never touches the mutable
///   accumulators.
///
/// Persistence:
///   %LOCALAPPDATA%\ExplainMyPC\baseline.json is read once at construction
///   and written every <see cref="PersistIntervalTicks"/> ticks (≈ 5 min).
///   A failed write is silently swallowed — the in-memory model is always
///   authoritative; the file is just a durability optimisation.
///
/// Idle CPU definition:
///   "Idle" is defined as system CPU% below 40 %. This captures background
///   service baseline, kernel overhead, and antivirus idle scans without
///   contaminating the model with user-initiated workload.
/// </summary>
public sealed class SystemBaselineService : ISystemBaselineService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Write baseline.json every N ticks (~5 min at 1.5 s/tick).</summary>
    private const int PersistIntervalTicks = 200;

    /// <summary>
    /// CPU % threshold for "idle" sampling.
    /// Observations above this are excluded from the idle-CPU accumulator.
    /// </summary>
    private const double IdleCpuThreshold = 40.0;

    /// <summary>Minimum temperature to treat a thermal reading as valid.</summary>
    private const double MinValidTemperatureCelsius = 20.0;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ITelemetryService _telemetry;

    // ── Welford accumulators (UI-thread only) ─────────────────────────────────

    private readonly WelfordStats _idleCpu      = new();
    private readonly WelfordStats _normalRam    = new();
    private readonly WelfordStats _normalThermal = new();
    private readonly WelfordStats _normalDiskIo  = new();

    // ── Supplementary state ───────────────────────────────────────────────────

    private int            _startupEntryCount;
    private int            _highImpactStartupCount;
    private long           _totalObservations;
    private DateTimeOffset _firstObservation = DateTimeOffset.Now;
    private int            _tickCount;

    // ── Cached snapshot ───────────────────────────────────────────────────────

    private MachineBaseline? _cachedBaseline;
    private bool              _baselineDirty = true;

    // ── Persistence path ──────────────────────────────────────────────────────

    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExplainMyPC");

    private static readonly string BaselineFilePath =
        Path.Combine(DataDirectory, "baseline.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented             = true,
        PropertyNamingPolicy      = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition    = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Construction ──────────────────────────────────────────────────────────

    public SystemBaselineService(ITelemetryService telemetry)
    {
        _telemetry = telemetry;
        TryLoadFromDisk();
    }

    // ── ISystemBaselineService ────────────────────────────────────────────────

    /// <inheritdoc/>
    public MachineBaseline? CurrentBaseline
    {
        get
        {
            if (_baselineDirty)
            {
                _cachedBaseline = BuildSnapshot();
                _baselineDirty  = false;
            }

            // Suppress the baseline during warm-up — rules must not act on
            // statistically weak data.
            return _totalObservations >= WelfordStats.MinAnomalyCount
                ? _cachedBaseline
                : null;
        }
    }

    public void Start() => _telemetry.ReadingAvailable += OnReadingAvailable;
    public void Stop()  => _telemetry.ReadingAvailable -= OnReadingAvailable;

    // ── Telemetry handler (UI thread) ─────────────────────────────────────────

    private void OnReadingAvailable(object? sender, TelemetrySnapshot snapshot)
    {
        _totalObservations++;

        // Idle CPU — only when the system is at low load.
        // High-load periods would inflate the idle estimate and make the
        // model insensitive to genuine anomalies.
        if (snapshot.CpuPercent < IdleCpuThreshold)
            _idleCpu.Update(snapshot.CpuPercent);

        // RAM — unconditional; reflects the machine's typical memory footprint.
        _normalRam.Update(snapshot.RamPercent);

        // Thermal — only when the ACPI sensor is active and reading is plausible.
        if (snapshot.CpuTemperatureAvailable &&
            snapshot.CpuTemperatureCelsius   >= MinValidTemperatureCelsius)
            _normalThermal.Update(snapshot.CpuTemperatureCelsius);

        // Disk I/O — combined read + write as a single composite metric.
        _normalDiskIo.Update(snapshot.DiskReadMbps + snapshot.DiskWriteMbps);

        // Startup patterns — track last observed counts.
        if (snapshot.StartupEntries.Count > 0)
        {
            _startupEntryCount = snapshot.StartupEntries.Count;
            _highImpactStartupCount = snapshot.StartupEntries
                .Count(static e => e.Impact == StartupImpact.High);
        }

        // Invalidate the cached snapshot.
        _baselineDirty = true;

        // Periodic persistence — capture a snapshot on the UI thread, then
        // write it on a background thread to avoid stalling the UI.
        if (++_tickCount % PersistIntervalTicks == 0)
        {
            var dto = CaptureStorageDto();
            _ = Task.Run(() => WriteDtoToDisk(dto));
        }
    }

    // ── Snapshot building ─────────────────────────────────────────────────────

    private MachineBaseline BuildSnapshot() => new()
    {
        // Idle CPU
        IdleCpuMean    = _idleCpu.Mean,
        IdleCpuStdDev  = _idleCpu.StdDev,
        IdleCpuSamples = _idleCpu.Count,

        // Normal RAM
        NormalRamMean    = _normalRam.Mean,
        NormalRamStdDev  = _normalRam.StdDev,
        NormalRamSamples = _normalRam.Count,

        // Thermal
        NormalThermalMean    = _normalThermal.Mean,
        NormalThermalStdDev  = _normalThermal.StdDev,
        NormalThermalSamples = _normalThermal.Count,

        // Disk I/O
        NormalDiskIoMean    = _normalDiskIo.Mean,
        NormalDiskIoStdDev  = _normalDiskIo.StdDev,
        NormalDiskIoSamples = _normalDiskIo.Count,

        // Startup
        StartupEntryCount      = _startupEntryCount,
        HighImpactStartupCount = _highImpactStartupCount,

        // Provenance
        TotalObservations = _totalObservations,
        LearnedSince      = _firstObservation,
    };

    // ── Persistence ───────────────────────────────────────────────────────────

    /// <summary>
    /// Captures a thread-safe serialization DTO from the current accumulator
    /// state. Must be called on the UI thread before handing off to a Task.
    /// </summary>
    private BaselineStorageDto CaptureStorageDto() => new()
    {
        Version              = 1,
        TotalObservations    = _totalObservations,
        FirstObservationTicks = _firstObservation.UtcTicks,
        StartupEntryCount    = _startupEntryCount,
        HighImpactStartupCount = _highImpactStartupCount,
        IdleCpu      = new StatsStorageDto(_idleCpu.Count,      _idleCpu.Mean,      _idleCpu.M2),
        NormalRam    = new StatsStorageDto(_normalRam.Count,    _normalRam.Mean,    _normalRam.M2),
        NormalThermal = new StatsStorageDto(_normalThermal.Count, _normalThermal.Mean, _normalThermal.M2),
        NormalDiskIo  = new StatsStorageDto(_normalDiskIo.Count,  _normalDiskIo.Mean,  _normalDiskIo.M2),
    };

    /// <summary>
    /// Writes a storage DTO to baseline.json. Called on a background Task.
    /// Failures are silently swallowed — the in-memory model is authoritative.
    /// </summary>
    private static void WriteDtoToDisk(BaselineStorageDto dto)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(BaselineFilePath, json);
        }
        catch
        {
            // Best-effort — never surface storage failures to the user.
        }
    }

    /// <summary>
    /// Attempts to restore the accumulators from baseline.json on startup.
    /// Silently ignored if the file does not exist, is corrupted, or belongs
    /// to an incompatible schema version.
    /// </summary>
    private void TryLoadFromDisk()
    {
        try
        {
            if (!File.Exists(BaselineFilePath)) return;

            var json = File.ReadAllText(BaselineFilePath);
            var dto  = JsonSerializer.Deserialize<BaselineStorageDto>(json, JsonOptions);

            if (dto is null || dto.Version != 1) return;

            // Restore accumulators from persisted state.
            Restore(_idleCpu,       dto.IdleCpu);
            Restore(_normalRam,     dto.NormalRam);
            Restore(_normalThermal, dto.NormalThermal);
            Restore(_normalDiskIo,  dto.NormalDiskIo);

            _totalObservations      = dto.TotalObservations;
            _startupEntryCount      = dto.StartupEntryCount;
            _highImpactStartupCount = dto.HighImpactStartupCount;

            if (dto.FirstObservationTicks > 0)
                _firstObservation = new DateTimeOffset(dto.FirstObservationTicks, TimeSpan.Zero);
        }
        catch
        {
            // Corrupted or missing file — start fresh. The model will rebuild.
        }
    }

    private static void Restore(WelfordStats stats, StatsStorageDto? dto)
    {
        if (dto is null || dto.Count <= 0) return;
        stats.Restore(dto.Count, dto.Mean, dto.M2);
    }
}

// ── Persistence DTOs ──────────────────────────────────────────────────────────

/// <summary>
/// JSON-serializable representation of the full baseline state.
/// Version field enables safe schema migration in future releases.
/// </summary>
internal sealed class BaselineStorageDto
{
    public int              Version                { get; set; }
    public long             TotalObservations      { get; set; }
    public long             FirstObservationTicks  { get; set; }
    public int              StartupEntryCount      { get; set; }
    public int              HighImpactStartupCount { get; set; }
    public StatsStorageDto? IdleCpu                { get; set; }
    public StatsStorageDto? NormalRam              { get; set; }
    public StatsStorageDto? NormalThermal          { get; set; }
    public StatsStorageDto? NormalDiskIo           { get; set; }
}

/// <summary>
/// JSON representation of a single <see cref="WelfordStats"/> accumulator.
/// Stores the three state values needed to resume accumulation exactly.
/// </summary>
internal sealed class StatsStorageDto
{
    public StatsStorageDto() { }
    public StatsStorageDto(long count, double mean, double m2)
    {
        Count = count;
        Mean  = mean;
        M2    = m2;
    }

    public long   Count { get; set; }
    public double Mean  { get; set; }
    public double M2    { get; set; }
}
