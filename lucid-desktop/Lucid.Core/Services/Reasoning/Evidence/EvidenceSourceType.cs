namespace Lucid.Services.Reasoning.Evidence;

/// <summary>
/// Identifies which subsystem produced a piece of evidence that
/// contributed to a cognitive inference.
///
/// Distinct from <see cref="Lucid.Services.Reasoning.EvidenceNodeType"/> which
/// is the visualization category for the operational evidence graph. This enum
/// describes the *originating system* for attribution and provenance tracking.
/// </summary>
public enum EvidenceSourceType
{
    /// <summary>Raw hardware telemetry (CPU %, RAM %, temperature, disk I/O).</summary>
    Telemetry = 0,

    /// <summary>A rule-triggered finding from <see cref="Lucid.Services.Intelligence.ISystemInsightEngine"/>.</summary>
    InsightEngine = 1,

    /// <summary>A behavioral anomaly from the anomaly detection service.</summary>
    AnomalyDetection = 2,

    /// <summary>A drift signal from the long-term Watchtower layer.</summary>
    DriftDetection = 3,

    /// <summary>A synthesized early warning combining anomaly and drift signals.</summary>
    EarlyWarning = 4,

    /// <summary>Session age, session history, or session-scoped patterns.</summary>
    SessionContext = 5,

    /// <summary>A previously executed remediation action from operation history.</summary>
    OperationHistory = 6,

    /// <summary>Process-level behavioral intelligence (attribution, behavioral flags).</summary>
    ProcessIntelligence = 7,

    /// <summary>Trust posture, governance state, or consent violations.</summary>
    GovernanceState = 8,

    /// <summary>Historical reasoning memory — a pattern seen in prior sessions.</summary>
    ReasoningMemory = 9,

    /// <summary>A forecast produced by the intelligence forecasting layer.</summary>
    ForecastEngine = 10,
}
