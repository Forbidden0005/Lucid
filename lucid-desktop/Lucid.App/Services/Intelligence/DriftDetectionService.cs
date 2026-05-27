using Lucid.Services.Watchtower;

namespace Lucid.Services.Intelligence;

// ── Interface ───────────────────────────────────────────────────────────────────

/// <summary>
/// Adapts the Watchtower layer's drift observations into <see cref="DetectedDrift"/>
/// records for the Intelligence layer and ViewModels.
///
/// Drift represents gradual multi-day degradation — not momentary spikes.
/// </summary>
public interface IDriftDetectionService
{
    /// <summary>Currently active drift observations. Empty when no significant drift exists.</summary>
    IReadOnlyList<DetectedDrift> CurrentDrifts { get; }

    /// <summary>
    /// Raised on the UI thread whenever the active drift set changes.
    /// Fires at Watchtower cadence (~30-minute cycles).
    /// </summary>
    event EventHandler<IReadOnlyList<DetectedDrift>>? DriftsUpdated;
}

// ── Implementation ──────────────────────────────────────────────────────────────

/// <summary>
/// Subscribes to <see cref="OperationalWatchtowerService.SnapshotUpdated"/> and
/// adapts <see cref="DriftObservation"/> records into <see cref="DetectedDrift"/>.
///
/// Design notes:
/// • Stateless beyond the current list — no background timer.
/// • Only surfaces observations that are significant or non-stable.
/// • All events are raised on the UI thread (inherited from Watchtower).
/// </summary>
public sealed class DriftDetectionService : IDriftDetectionService
{
    private IReadOnlyList<DetectedDrift> _current = [];

    /// <inheritdoc/>
    public IReadOnlyList<DetectedDrift> CurrentDrifts => _current;

    /// <inheritdoc/>
    public event EventHandler<IReadOnlyList<DetectedDrift>>? DriftsUpdated;

    public DriftDetectionService(OperationalWatchtowerService watchtower)
    {
        watchtower.SnapshotUpdated += OnSnapshotUpdated;

        // Seed immediately from any existing snapshot so back-navigation is instant.
        if (watchtower.LastSnapshot is { } snap)
            ApplySnapshot(snap);
    }

    // ── Event handler ─────────────────────────────────────────────────────────

    private void OnSnapshotUpdated(object? sender, WatchtowerSnapshot snap)
        => ApplySnapshot(snap);

    // ── Core logic ────────────────────────────────────────────────────────────

    private void ApplySnapshot(WatchtowerSnapshot snap)
    {
        if (!snap.HasSufficientHistory)
        {
            if (_current.Count > 0)
            {
                _current = [];
                DriftsUpdated?.Invoke(this, _current);
            }
            return;
        }

        var drifts = snap.DriftObservations
            .Where(d => d.IsSignificant || d.Direction != DriftDirection.Stable)
            .Select(AdaptDrift)
            .OrderByDescending(d => (int)d.Severity)
            .ToList();

        _current = drifts;
        DriftsUpdated?.Invoke(this, _current);
    }

    private static DetectedDrift AdaptDrift(DriftObservation obs)
    {
        var direction = obs.Direction switch
        {
            DriftDirection.Improving => AnomalyDriftDirection.Improving,
            DriftDirection.Drifting  => AnomalyDriftDirection.Drifting,
            DriftDirection.Elevated  => AnomalyDriftDirection.Elevated,
            _                        => AnomalyDriftDirection.Stable,
        };

        var severity = obs.Direction switch
        {
            DriftDirection.Elevated  => obs.IsSignificant
                                            ? AnomalyIntelligenceSeverity.High
                                            : AnomalyIntelligenceSeverity.Elevated,
            DriftDirection.Drifting  => AnomalyIntelligenceSeverity.Moderate,
            DriftDirection.Improving => AnomalyIntelligenceSeverity.Low,
            _                        => AnomalyIntelligenceSeverity.Low,
        };

        var summary = obs.Direction switch
        {
            DriftDirection.Elevated  =>
                $"{obs.MetricName} usage has drifted noticeably upward over the past week.",
            DriftDirection.Drifting  =>
                $"{obs.MetricName} shows a gradual upward trend — worth monitoring.",
            DriftDirection.Improving =>
                $"{obs.MetricName} has been improving compared to the 30-day average.",
            _ =>
                $"{obs.MetricName} usage is stable relative to the 30-day average.",
        };

        var hint = obs.Direction is DriftDirection.Elevated or DriftDirection.Drifting
            ? $"Review {obs.MetricName} trends to understand what's driving the increase."
            : string.Empty;

        return new DetectedDrift(
            MetricName:         obs.MetricName,
            MetricGlyph:        obs.Glyph,
            Direction:          direction,
            SevenDayAvg:        obs.SevenDayAvg,
            ThirtyDayAvg:       obs.ThirtyDayAvg,
            ChangePercent:      obs.ChangePercent,
            DriftLabel:         obs.DriftLabel,
            Severity:           severity,
            Summary:            summary,
            RecommendationHint: hint);
    }
}
