namespace ExplainMyPC.Services.Intelligence;

/// <summary>
/// Which hardware resource a process is attributed to consuming.
/// </summary>
public enum AttributionKind
{
    /// <summary>Process identified as a top CPU consumer via direct measurement.</summary>
    Cpu,

    /// <summary>Process identified as a top RAM consumer via direct measurement.</summary>
    Ram,

    /// <summary>
    /// Process inferred (not measured) as a GPU consumer based on its known
    /// GPU-intensive profile. Confidence will be lower than Cpu/Ram.
    /// </summary>
    Gpu,

    /// <summary>Process identified as a top disk I/O consumer.</summary>
    Disk,
}

/// <summary>
/// Identifies a specific process as a likely contributor to a hardware resource
/// insight finding.
///
/// Produced by <see cref="ProcessAttributionHelper"/> and attached to
/// <see cref="SystemInsight.AttributedProcesses"/> so the UI can display
/// "Chrome is using 8.1 GB RAM" directly on the insight card.
///
/// GPU attributions are always heuristic (confidence ~55 %) because
/// per-process GPU data is not available from standard perf counters in .NET.
/// </summary>
public sealed record ProcessAttribution(
    /// <summary>
    /// Human-readable process name shown in the UI, e.g. "Google Chrome".
    /// Sourced from <see cref="Helpers.ProcessSample.DisplayName"/>.
    /// </summary>
    string DisplayName,

    /// <summary>Which hardware resource this process is attributed to.</summary>
    AttributionKind Kind,

    /// <summary>
    /// Numeric usage value. Interpretation depends on <see cref="Kind"/>:
    ///   Cpu  → CPU percentage (0–100)
    ///   Ram  → gigabytes of Working Set
    ///   Gpu  → 0 (not available; heuristic inference only)
    ///   Disk → MB/s (reserved for future use)
    /// </summary>
    float UsageValue,

    /// <summary>Unit label matching <see cref="UsageValue"/>, e.g. "%" or "GB".</summary>
    string UsageUnit,

    /// <summary>
    /// Confidence in this attribution, expressed as a percentage.
    ///   90 % — directly measured (Cpu, Ram)
    ///   55 % — heuristically inferred (Gpu)
    /// </summary>
    int ConfidencePercent)
{
    /// <summary>Short resource label for the UI badge, e.g. "CPU", "RAM", "GPU".</summary>
    public string KindLabel => Kind switch
    {
        AttributionKind.Cpu  => "CPU",
        AttributionKind.Ram  => "RAM",
        AttributionKind.Gpu  => "GPU",
        AttributionKind.Disk => "Disk",
        _                    => "Unknown",
    };

    /// <summary>
    /// Formatted usage string ready for binding, e.g. "82%", "8.1 GB", "Detected".
    /// GPU heuristic attributions show "Detected" since actual usage is unknown.
    /// Low-confidence (GPU) values are prefixed with "~" to signal inference.
    /// </summary>
    public string UsageText => Kind switch
    {
        AttributionKind.Ram  => $"{UsageValue:F1} {UsageUnit}",
        AttributionKind.Cpu  => $"{UsageValue:F0}{UsageUnit}",
        AttributionKind.Gpu  when UsageValue > 0 => $"~{UsageValue:F0}{UsageUnit}",
        AttributionKind.Gpu  => "Detected",
        _                    => UsageValue > 0 ? $"{UsageValue:F0} {UsageUnit}" : "Active",
    };
}
