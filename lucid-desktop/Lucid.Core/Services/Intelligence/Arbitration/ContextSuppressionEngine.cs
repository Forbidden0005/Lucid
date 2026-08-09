using Lucid.Services.Intelligence;
using Lucid.Services.Reasoning.Context;

namespace Lucid.Services.Intelligence.Arbitration;

/// <summary>
/// Determines whether a recommendation should be suppressed given the
/// current operational context.
///
/// Context suppression prevents Lucid from nagging users about expected
/// behavior during known workloads:
///   - "Reduce CPU usage" suppressed during gaming
///   - "High disk usage detected" suppressed during Windows Update
///   - "Thermal alert" suppressed during heavy rendering at expected temperatures
///
/// Suppression is always labeled and never silent — suppressed recommendations
/// are logged and available in the Diagnostics page for transparency.
///
/// Stateless pure utility — all inputs are passed explicitly.
/// </summary>
public static class ContextSuppressionEngine
{
    /// <summary>
    /// Evaluates whether a recommendation should be suppressed given the
    /// current context profile.
    /// </summary>
    /// <param name="action">The recommended action to evaluate.</param>
    /// <param name="insightCategory">Category of the source insight.</param>
    /// <param name="context">Current operational context. Null means no context established.</param>
    /// <returns>
    /// Tuple of (IsSuppressed, Reason). If not suppressed, Reason is null.
    /// </returns>
    public static (bool IsSuppressed, string? Reason) Evaluate(
        SystemAction   action,
        string         insightCategory,
        ContextProfile? context)
    {
        if (context is null)
            return (false, null);

        var cat = insightCategory.ToLowerInvariant();
        var aid = (action.Id ?? "").ToLowerInvariant();

        // ── Post-boot transient suppression ───────────────────────────────────
        if (context.IsPostBootTransient)
        {
            if (cat.Contains("startup") || cat.Contains("cpu") || cat.Contains("ram"))
                return (true, "Post-boot transient — system is still initializing");
        }

        // ── Context-aware CPU suppression ─────────────────────────────────────
        if (context.HighCpuIsExpected && (cat.Contains("cpu") || cat.Contains("performance")))
        {
            return (true,
                $"High CPU is expected during {context.WorkloadLabel} — recommendation suppressed");
        }

        // ── Disk I/O suppression during updates ───────────────────────────────
        if (context.HighDiskIoIsExpected && (cat.Contains("disk") || cat.Contains("storage")))
        {
            return (true,
                $"High disk activity is expected during {context.WorkloadLabel}");
        }

        // ── Thermal suppression during high-load workloads ────────────────────
        if (context.ThermalElevationIsExpected && cat.Contains("thermal"))
        {
            return (true,
                $"Thermal elevation is expected during {context.WorkloadLabel}");
        }

        // ── Redundancy check: same domain recently acted on ────────────────────
        // (Deduplication window enforcement is handled by RecommendationArbitrator.
        // This engine only applies context-based suppression.)

        return (false, null);
    }
}
