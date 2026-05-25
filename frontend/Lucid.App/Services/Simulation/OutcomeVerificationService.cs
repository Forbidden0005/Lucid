using System.Text.Json;

namespace Lucid.Services.Simulation;

// ── Models ─────────────────────────────────────────────────────────────────────

/// <summary>
/// A simulation that is waiting for its measurement window to elapse
/// so predicted vs actual outcomes can be compared.
///
/// Stored in-memory only — pending verifications are ephemeral.
/// If the app closes before the window elapses, the pending record is lost.
/// </summary>
public sealed record PendingSimulationVerification(
    Guid            SnapshotId,
    string          ScenarioType,
    DateTimeOffset  SimulatedAt,
    DateTimeOffset  MeasureAfter,
    double          RamBefore,
    double          CpuBefore,
    double          PredictedRamDelta,
    double          PredictedCpuDelta);

/// <summary>
/// Measured prediction accuracy for a completed simulation.
///
/// <para>
/// Accuracy is computed by comparing what the simulation predicted
/// (e.g. "RAM will drop by 12%") against what actually happened
/// when current telemetry is sampled after the measurement window.
/// </para>
///
/// <para>
/// <see cref="InterventionDetected"/> is true when the actual metric
/// moved in the same direction as the prediction — indicating the
/// user likely performed the action.
/// </para>
/// </summary>
public sealed record SimulationOutcome(
    Guid            SnapshotId,
    DateTimeOffset  EvaluatedAt,
    string          ScenarioType,
    double          PredictedRamDelta,
    double          ActualRamDelta,
    double          PredictedCpuDelta,
    double          ActualCpuDelta,
    double          PredictionAccuracy,    // 0–100
    bool            InterventionDetected); // metrics moved in the predicted direction

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Tracks predicted vs actual outcomes for completed simulations.
///
/// <para>
/// Usage pattern:
///   1. Call <see cref="Register"/> immediately after a simulation completes.
///   2. Call <see cref="TryEvaluatePending"/> at the start of each new simulation
///      (using the latest telemetry reading). Any pending verifications whose
///      measurement window has elapsed are evaluated and removed.
///   3. Call <see cref="GetOutcome"/> to retrieve the accuracy for a snapshot.
/// </para>
/// </summary>
public interface IOutcomeVerificationService
{
    /// <summary>
    /// Registers a new pending verification for the specified snapshot.
    /// Ignored if a verification for this snapshot already exists.
    /// </summary>
    void Register(
        Guid     snapshotId,
        string   scenarioType,
        double   ramBefore,
        double   cpuBefore,
        double   predictedRamDelta,
        double   predictedCpuDelta,
        TimeSpan measurementWindow);

    /// <summary>
    /// Evaluates all pending verifications whose measurement window has elapsed,
    /// using the supplied telemetry as the "actual" measurement point.
    /// Fire-and-forget safe — persists results asynchronously.
    /// </summary>
    void TryEvaluatePending(double currentRam, double currentCpu);

    /// <summary>
    /// Returns the outcome for the specified snapshot, or null if not yet evaluated.
    /// </summary>
    SimulationOutcome? GetOutcome(Guid snapshotId);

    /// <summary>
    /// Returns the mean prediction accuracy (0–100) across all outcomes for
    /// the specified scenario type. Returns -1 when no outcomes exist yet.
    /// </summary>
    double GetScenarioAccuracy(string scenarioType);

    /// <summary>Number of evaluated outcomes for the specified scenario type.</summary>
    int GetScenarioSampleCount(string scenarioType);

    /// <summary>All evaluated outcomes, newest first.</summary>
    IReadOnlyList<SimulationOutcome> GetAllOutcomes();
}

// ── Implementation ─────────────────────────────────────────────────────────────

/// <summary>
/// JSON-backed prediction accuracy tracker for simulation outcomes.
///
/// Design principles:
///   • Pending verifications are in-memory only (ephemeral).
///   • Evaluated outcomes are persisted to
///     %LOCALAPPDATA%\Lucid\simulation_outcomes.json (max 50).
///   • All writes are best-effort — never throws, never blocks.
///   • Accuracy is computed using RAM as the primary metric (most change-sensitive)
///     with CPU as a secondary metric when the simulation predicted significant CPU change.
///   • Cold-start safe: missing or corrupt file yields an empty outcome set.
/// </summary>
public sealed class OutcomeVerificationService : IOutcomeVerificationService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int MaxOutcomes = 50;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucid",
        "simulation_outcomes.json");

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = false };

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly List<PendingSimulationVerification> _pending   = new();
    private readonly List<SimulationOutcome>             _outcomes  = new();
    private readonly Dictionary<string, List<double>>   _accuracy  = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim                       _writeLock = new(1, 1);

    // ── Constructor ───────────────────────────────────────────────────────────

    public OutcomeVerificationService()
    {
        LoadFromDisk();
    }

    // ── IOutcomeVerificationService ───────────────────────────────────────────

    /// <inheritdoc/>
    public void Register(
        Guid snapshotId, string scenarioType,
        double ramBefore, double cpuBefore,
        double predictedRamDelta, double predictedCpuDelta,
        TimeSpan measurementWindow)
    {
        if (_pending.Any(p => p.SnapshotId == snapshotId)) return;

        _pending.Add(new PendingSimulationVerification(
            SnapshotId:        snapshotId,
            ScenarioType:      scenarioType,
            SimulatedAt:       DateTimeOffset.Now,
            MeasureAfter:      DateTimeOffset.Now + measurementWindow,
            RamBefore:         ramBefore,
            CpuBefore:         cpuBefore,
            PredictedRamDelta: predictedRamDelta,
            PredictedCpuDelta: predictedCpuDelta));
    }

    /// <inheritdoc/>
    public void TryEvaluatePending(double currentRam, double currentCpu)
    {
        var now  = DateTimeOffset.Now;
        var done = new List<PendingSimulationVerification>();

        foreach (var p in _pending)
        {
            if (now < p.MeasureAfter) continue;

            double actualRamDelta = currentRam - p.RamBefore;
            double actualCpuDelta = currentCpu - p.CpuBefore;
            double accuracy       = ComputeAccuracy(
                p.PredictedRamDelta, actualRamDelta,
                p.PredictedCpuDelta, actualCpuDelta);

            var outcome = new SimulationOutcome(
                SnapshotId:           p.SnapshotId,
                EvaluatedAt:          now,
                ScenarioType:         p.ScenarioType,
                PredictedRamDelta:    p.PredictedRamDelta,
                ActualRamDelta:       actualRamDelta,
                PredictedCpuDelta:    p.PredictedCpuDelta,
                ActualCpuDelta:       actualCpuDelta,
                PredictionAccuracy:   accuracy,
                InterventionDetected: InterventionDetected(p.PredictedRamDelta, actualRamDelta));

            _outcomes.Insert(0, outcome);
            if (_outcomes.Count > MaxOutcomes)
                _outcomes.RemoveAt(_outcomes.Count - 1);

            if (!_accuracy.ContainsKey(p.ScenarioType))
                _accuracy[p.ScenarioType] = new List<double>();
            _accuracy[p.ScenarioType].Add(accuracy);

            done.Add(p);
        }

        foreach (var d in done) _pending.Remove(d);
        if (done.Count > 0) _ = PersistAsync();
    }

    /// <inheritdoc/>
    public SimulationOutcome? GetOutcome(Guid snapshotId) =>
        _outcomes.FirstOrDefault(o => o.SnapshotId == snapshotId);

    /// <inheritdoc/>
    public double GetScenarioAccuracy(string scenarioType)
    {
        if (!_accuracy.TryGetValue(scenarioType, out var list) || list.Count == 0)
            return -1;
        return list.Average();
    }

    /// <inheritdoc/>
    public int GetScenarioSampleCount(string scenarioType) =>
        _accuracy.TryGetValue(scenarioType, out var list) ? list.Count : 0;

    /// <inheritdoc/>
    public IReadOnlyList<SimulationOutcome> GetAllOutcomes() => _outcomes.AsReadOnly();

    // ── Accuracy computation ───────────────────────────────────────────────────

    /// <summary>
    /// Computes a 0–100 accuracy score comparing predicted vs actual deltas.
    ///
    /// RAM is the primary metric (weight 0.6 or 1.0 when CPU prediction is small).
    /// CPU is included when the simulation predicted significant CPU change (> 5%).
    ///
    /// A scale floor of 5% prevents division-by-zero and avoids inflating accuracy
    /// when both predicted and actual deltas are near zero.
    /// </summary>
    private static double ComputeAccuracy(
        double predictedRam, double actualRam,
        double predictedCpu, double actualCpu)
    {
        double ramError    = Math.Abs(predictedRam - actualRam);
        double ramScale    = Math.Max(5.0, Math.Max(Math.Abs(predictedRam), Math.Abs(actualRam)));
        double ramAccuracy = Math.Max(0, 100.0 - ramError / ramScale * 100.0);

        if (Math.Abs(predictedCpu) > 5)
        {
            double cpuError    = Math.Abs(predictedCpu - actualCpu);
            double cpuScale    = Math.Max(5.0, Math.Max(Math.Abs(predictedCpu), Math.Abs(actualCpu)));
            double cpuAccuracy = Math.Max(0, 100.0 - cpuError / cpuScale * 100.0);
            return ramAccuracy * 0.6 + cpuAccuracy * 0.4;
        }

        return ramAccuracy;
    }

    /// <summary>
    /// Returns true when the actual metric moved in the same direction as the
    /// prediction, indicating the user likely performed the modeled action.
    /// Only meaningful when the predicted delta is >= 2% (to avoid noise).
    /// </summary>
    private static bool InterventionDetected(double predictedDelta, double actualDelta)
    {
        if (Math.Abs(predictedDelta) < 2) return false;
        return Math.Sign(predictedDelta) == Math.Sign(actualDelta)
               && Math.Abs(actualDelta) > 1;
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            var json   = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<List<SimulationOutcome>>(json);
            if (loaded is null) return;

            _outcomes.AddRange(loaded);

            foreach (var o in loaded)
            {
                if (!_accuracy.ContainsKey(o.ScenarioType))
                    _accuracy[o.ScenarioType] = new List<double>();
                _accuracy[o.ScenarioType].Add(o.PredictionAccuracy);
            }
        }
        catch
        {
            // Cold-start safe — empty outcomes if file is missing or corrupt
        }
    }

    private async Task PersistAsync()
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(_outcomes, JsonOptions);
            await File.WriteAllTextAsync(FilePath, json).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — a failed write is not surfaced to the user
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
