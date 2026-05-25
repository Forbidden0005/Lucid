namespace Lucid.Services.Intelligence;

// ── Shared severity ─────────────────────────────────────────────────────────────

/// <summary>
/// Severity tier shared across anomaly, drift, and early-warning records.
/// Uses probability-aware, non-alarming language — never "critical" or "dangerous".
/// </summary>
public enum AnomalyIntelligenceSeverity
{
    Low      = 0,
    Moderate = 1,
    Elevated = 2,
    High     = 3,
}

// ── Anomaly drift direction ─────────────────────────────────────────────────────

/// <summary>
/// Direction of long-term operational drift for a metric.
/// Mirrors <see cref="Lucid.Services.Watchtower.DriftDirection"/> but lives
/// in the Intelligence layer so upper layers don't need the Watchtower namespace.
/// </summary>
public enum AnomalyDriftDirection
{
    Stable    = 0,
    Improving = 1,
    Drifting  = 2,
    Elevated  = 3,
}

// ── BehavioralBaseline ──────────────────────────────────────────────────────────

/// <summary>
/// A summarized snapshot of what's "normal" for this machine, derived from the
/// live Welford model in <see cref="Lucid.Services.Baseline.MachineBaseline"/>.
/// Exposed by <see cref="IBehavioralBaselineService"/> for display and comparison.
/// </summary>
public sealed record BehavioralBaseline(
    double   IdleCpuMean,
    double   IdleCpuStdDev,
    long     IdleCpuSamples,

    double   NormalRamMean,
    double   NormalRamStdDev,
    long     NormalRamSamples,

    double   NormalThermalMean,
    double   NormalThermalStdDev,
    long     ThermalSamples,

    /// <summary>True when both CPU and RAM accumulators have enough data (≥ 200 obs).</summary>
    bool     IsReliable,

    DateTime CapturedAt)
{
    /// <summary>Human-readable CPU floor, e.g. "~12% at idle".</summary>
    public string CpuFloorLabel =>
        IdleCpuSamples >= 50 ? $"~{IdleCpuMean:0}% at idle" : "Calibrating…";

    /// <summary>Human-readable RAM baseline, e.g. "~58% typical".</summary>
    public string RamBaselineLabel =>
        NormalRamSamples >= 50 ? $"~{NormalRamMean:0}% typical" : "Calibrating…";

    /// <summary>Human-readable thermal baseline, e.g. "~52°C typical".</summary>
    public string ThermalBaselineLabel =>
        ThermalSamples >= 50 ? $"~{NormalThermalMean:0}°C typical" : "N/A";

    /// <summary>Total sample count across all tracked metrics.</summary>
    public long TotalSamples => IdleCpuSamples + NormalRamSamples + ThermalSamples;

    /// <summary>Label showing how many samples have been collected.</summary>
    public string SampleCountLabel => $"{TotalSamples:N0} observations";

    /// <summary>Reliability status label.</summary>
    public string ReliabilityLabel => IsReliable ? "Baseline active" : "Calibrating baseline…";

    /// <summary>Hex color for reliability indicator.</summary>
    public string ReliabilityColor => IsReliable ? "#34D399" : "#6B7280";

    /// <summary>Relative capture time, e.g. "Just now".</summary>
    public string CapturedLabel => FormatRelative(CapturedAt);

    private static string FormatRelative(DateTime dt)
    {
        var age = DateTime.UtcNow - dt;
        return age.TotalMinutes < 1 ? "Just now"
             : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes}m ago"
             : age.TotalHours   < 24 ? $"{(int)age.TotalHours}h ago"
             : $"{(int)age.TotalDays}d ago";
    }
}

// ── DetectedDrift ───────────────────────────────────────────────────────────────

/// <summary>
/// A long-term operational drift observation for a single metric,
/// adapted from the Watchtower layer's <see cref="Lucid.Services.Watchtower.DriftObservation"/>.
/// Represents gradual multi-day degradation trends, not momentary spikes.
/// </summary>
public sealed record DetectedDrift(
    string                      MetricName,
    string                      MetricGlyph,
    AnomalyDriftDirection       Direction,
    double?                     SevenDayAvg,
    double?                     ThirtyDayAvg,
    double                      ChangePercent,
    string                      DriftLabel,
    AnomalyIntelligenceSeverity Severity,
    string                      Summary,
    string                      RecommendationHint)
{
    /// <summary>Direction glyph for UI display.</summary>
    public string DirectionGlyph => Direction switch
    {
        AnomalyDriftDirection.Improving => "↑",
        AnomalyDriftDirection.Drifting  => "↘",
        AnomalyDriftDirection.Elevated  => "↓",
        _                               => "→",
    };

    /// <summary>Hex severity color.</summary>
    public string SeverityColor => Severity switch
    {
        AnomalyIntelligenceSeverity.High     => "#F87171",
        AnomalyIntelligenceSeverity.Elevated => "#FBBF24",
        AnomalyIntelligenceSeverity.Moderate => "#F59E0B",
        _                                    => "#6B7280",
    };

    /// <summary>Hex direction color — green for improving, amber/red for worsening.</summary>
    public string DirectionColor => Direction switch
    {
        AnomalyDriftDirection.Improving => "#34D399",
        AnomalyDriftDirection.Drifting  => "#FBBF24",
        AnomalyDriftDirection.Elevated  => "#F87171",
        _                               => "#9CA3AF",
    };

    /// <summary>Severity text label.</summary>
    public string SeverityLabel => Severity switch
    {
        AnomalyIntelligenceSeverity.High     => "Significant",
        AnomalyIntelligenceSeverity.Elevated => "Elevated",
        AnomalyIntelligenceSeverity.Moderate => "Moderate",
        _                                    => "Low",
    };

    /// <summary>7-day vs 30-day comparison, e.g. "38% avg (7d) vs 32% (30d)".</summary>
    public string AvgComparisonLabel =>
        SevenDayAvg.HasValue && ThirtyDayAvg.HasValue
            ? $"{SevenDayAvg.Value:0}% avg (7d) vs {ThirtyDayAvg.Value:0}% (30d)"
            : DriftLabel;

    /// <summary>Signed change string, e.g. "+12% worse".</summary>
    public string ChangeLabel => ChangePercent switch
    {
        > 0  => $"+{ChangePercent:0}% worse",
        < 0  => $"{ChangePercent:0}% better",
        _    => "Stable",
    };
}

// ── DetectedAnomaly ─────────────────────────────────────────────────────────────

/// <summary>
/// A short-term behavioral anomaly detected by comparing live telemetry to the
/// machine's learned baseline via z-score thresholds.
/// Always describes observations — never claims certainty.
/// </summary>
public sealed record DetectedAnomaly(
    string                      Metric,
    double                      ExpectedMean,
    double                      ExpectedStdDev,
    double                      ActualValue,
    double                      DivergenceScore,
    AnomalyIntelligenceSeverity Severity,
    string                      Description,
    DateTime                    DetectedAt)
{
    /// <summary>Segoe MDL2 glyph for the affected metric.</summary>
    public string MetricGlyph => Metric switch
    {
        "CPU"     => "",
        "RAM"     => "",
        "Thermal" => "",
        "Disk"    => "",
        _         => "",
    };

    /// <summary>Hex severity color.</summary>
    public string SeverityColor => Severity switch
    {
        AnomalyIntelligenceSeverity.High     => "#F87171",
        AnomalyIntelligenceSeverity.Elevated => "#FBBF24",
        AnomalyIntelligenceSeverity.Moderate => "#F59E0B",
        _                                    => "#6B7280",
    };

    /// <summary>Background tint for severity badge.</summary>
    public string SeverityBadgeColor => Severity switch
    {
        AnomalyIntelligenceSeverity.High     => "#7F1D1D",
        AnomalyIntelligenceSeverity.Elevated => "#78350F",
        AnomalyIntelligenceSeverity.Moderate => "#451A03",
        _                                    => "#1F2937",
    };

    /// <summary>Severity text label.</summary>
    public string SeverityLabel => Severity switch
    {
        AnomalyIntelligenceSeverity.High     => "Significant",
        AnomalyIntelligenceSeverity.Elevated => "Elevated",
        AnomalyIntelligenceSeverity.Moderate => "Moderate",
        _                                    => "Low",
    };

    /// <summary>Expected range display, e.g. "~12%–26%".</summary>
    public string ExpectedRangeLabel =>
        $"~{Math.Max(0, ExpectedMean - ExpectedStdDev):0}–{ExpectedMean + ExpectedStdDev:0}";

    /// <summary>Actual value with unit, e.g. "47%".</summary>
    public string ActualValueLabel => Metric == "Thermal"
        ? $"{ActualValue:0}°C"
        : $"{ActualValue:0}%";

    /// <summary>Expected mean with unit.</summary>
    public string ExpectedMeanLabel => Metric == "Thermal"
        ? $"~{ExpectedMean:0}°C typical"
        : $"~{ExpectedMean:0}% typical";

    /// <summary>Z-score divergence display, e.g. "2.4σ above baseline".</summary>
    public string DivergenceLabel => DivergenceScore >= 0
        ? $"{DivergenceScore:F1}σ above baseline"
        : $"{Math.Abs(DivergenceScore):F1}σ below baseline";

    /// <summary>Relative detection time.</summary>
    public string DetectedLabel => FormatRelative(DetectedAt);

    private static string FormatRelative(DateTime dt)
    {
        var age = DateTime.UtcNow - dt;
        return age.TotalSeconds < 60 ? "Just now"
             : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes}m ago"
             : age.TotalHours   < 24 ? $"{(int)age.TotalHours}h ago"
             : $"{(int)age.TotalDays}d ago";
    }
}

// ── EarlyWarning ────────────────────────────────────────────────────────────────

/// <summary>
/// Severity tier for early warnings synthesized from anomalies, drifts, and Watchtower alerts.
/// </summary>
public enum EarlyWarningSeverity
{
    Info     = 0,
    Advisory = 1,
    Warning  = 2,
    Elevated = 3,
}

/// <summary>
/// A synthesized proactive awareness signal generated by combining anomaly detection,
/// drift analysis, and Watchtower alerts.
/// Always uses confidence-aware, non-alarming language.
/// </summary>
public sealed record EarlyWarning(
    string             WarningId,
    string             Title,
    string             Explanation,
    EarlyWarningSeverity Severity,
    int                ConfidencePercent,
    string?            AffectedMetric,
    string?            ActionHint,
    DateTime           DetectedAt)
{
    /// <summary>Hex severity color.</summary>
    public string SeverityColor => Severity switch
    {
        EarlyWarningSeverity.Elevated => "#F87171",
        EarlyWarningSeverity.Warning  => "#FBBF24",
        EarlyWarningSeverity.Advisory => "#60A5FA",
        _                             => "#6B7280",
    };

    /// <summary>Background badge hex color (darker tint).</summary>
    public string SeverityBadgeColor => Severity switch
    {
        EarlyWarningSeverity.Elevated => "#7F1D1D",
        EarlyWarningSeverity.Warning  => "#78350F",
        EarlyWarningSeverity.Advisory => "#1E3A5F",
        _                             => "#1F2937",
    };

    /// <summary>Severity text label.</summary>
    public string SeverityLabel => Severity switch
    {
        EarlyWarningSeverity.Elevated => "Elevated",
        EarlyWarningSeverity.Warning  => "Warning",
        EarlyWarningSeverity.Advisory => "Advisory",
        _                             => "Info",
    };

    /// <summary>Segoe MDL2 glyph for this warning's severity.</summary>
    public string Glyph => Severity switch
    {
        EarlyWarningSeverity.Elevated => "",
        EarlyWarningSeverity.Warning  => "",
        EarlyWarningSeverity.Advisory => "",
        _                             => "",
    };

    /// <summary>Metric icon glyph when AffectedMetric is set.</summary>
    public string MetricGlyph => AffectedMetric switch
    {
        "CPU"     => "",
        "RAM"     => "",
        "Disk"    => "",
        "Thermal" => "",
        _         => "",
    };

    /// <summary>Confidence display, e.g. "82% confidence".</summary>
    public string ConfidenceLabel => $"{ConfidencePercent}% confidence";

    /// <summary>Relative detection time.</summary>
    public string DetectedLabel => FormatRelative(DetectedAt);

    private static string FormatRelative(DateTime dt)
    {
        var age = DateTime.UtcNow - dt;
        return age.TotalSeconds < 60 ? "Just now"
             : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes}m ago"
             : age.TotalHours   < 24 ? $"{(int)age.TotalHours}h ago"
             : $"{(int)age.TotalDays}d ago";
    }
}

// ── MachinePersonality ──────────────────────────────────────────────────────────

/// <summary>
/// A deterministic classification of this machine's observed operational behavior.
/// Computed from historical trends, anomaly patterns, and drift signals.
/// </summary>
public enum MachinePersonality
{
    /// <summary>Consistent, predictable, low-noise operation.</summary>
    Stable               = 0,

    /// <summary>Frequent short-lived spikes — workload-driven variability.</summary>
    Volatile             = 1,

    /// <summary>Consistently high resource utilization across CPU and RAM.</summary>
    ResourceHeavy        = 2,

    /// <summary>Progressive increase in baseline metrics over weeks.</summary>
    GraduallyDegrading   = 3,

    /// <summary>Thermal patterns suggest inadequate cooling or ventilation.</summary>
    ThermallyConstrained = 4,

    /// <summary>Bursty activity profile — quiet periods mixed with heavy loads.</summary>
    BurstHeavy           = 5,

    /// <summary>Insufficient data to classify reliably.</summary>
    Unknown              = 6,
}

/// <summary>
/// A full report on this machine's operational personality, with supporting evidence
/// and display-ready strings.
/// </summary>
public sealed record MachinePersonalityReport(
    MachinePersonality Personality,
    string             Title,
    string             Description,
    string[]           SupportingEvidence,
    int                ConfidencePercent,
    DateTime           ComputedAt)
{
    /// <summary>Segoe MDL2 glyph for the personality type.</summary>
    public string PersonalityGlyph => Personality switch
    {
        MachinePersonality.Stable               => "",
        MachinePersonality.Volatile             => "",
        MachinePersonality.ResourceHeavy        => "",
        MachinePersonality.GraduallyDegrading   => "",
        MachinePersonality.ThermallyConstrained => "",
        MachinePersonality.BurstHeavy           => "",
        _                                       => "",
    };

    /// <summary>Hex accent color for the personality badge.</summary>
    public string PersonalityColor => Personality switch
    {
        MachinePersonality.Stable               => "#34D399",
        MachinePersonality.Volatile             => "#FBBF24",
        MachinePersonality.ResourceHeavy        => "#F87171",
        MachinePersonality.GraduallyDegrading   => "#FB923C",
        MachinePersonality.ThermallyConstrained => "#F87171",
        MachinePersonality.BurstHeavy           => "#A78BFA",
        _                                       => "#6B7280",
    };

    /// <summary>Background tint for personality badge.</summary>
    public string PersonalityBadgeColor => Personality switch
    {
        MachinePersonality.Stable               => "#064E3B",
        MachinePersonality.Volatile             => "#78350F",
        MachinePersonality.ResourceHeavy        => "#7F1D1D",
        MachinePersonality.GraduallyDegrading   => "#431407",
        MachinePersonality.ThermallyConstrained => "#7F1D1D",
        MachinePersonality.BurstHeavy           => "#2E1065",
        _                                       => "#1F2937",
    };

    /// <summary>Confidence display, e.g. "74% confidence".</summary>
    public string ConfidenceLabel => $"{ConfidencePercent}% confidence";

    /// <summary>Relative computation time.</summary>
    public string ComputedLabel => FormatRelative(ComputedAt);

    private static string FormatRelative(DateTime dt)
    {
        var age = DateTime.UtcNow - dt;
        return age.TotalMinutes < 1  ? "Just now"
             : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes}m ago"
             : $"{(int)age.TotalHours}h ago";
    }
}
