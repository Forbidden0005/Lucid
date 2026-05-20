namespace ExplainMyPC.Services.Security;

// ── Trust classification ──────────────────────────────────────────────────────

/// <summary>
/// Confidence-aware trust classification for executable files and startup entries.
/// Values are ordered — higher integers represent lower trust.
/// </summary>
public enum TrustLevel
{
    /// <summary>Signed by Microsoft Corporation or a Windows system component.</summary>
    TrustedMicrosoft   = 0,

    /// <summary>Signed by a recognized commercial software vendor.</summary>
    SignedKnownVendor  = 1,

    /// <summary>Unsigned, but the executable name matches a widely-used application.</summary>
    UnsignedCommon     = 2,

    /// <summary>Unsigned and not recognized in the common-app catalogue.</summary>
    Unsigned           = 3,

    /// <summary>
    /// Unsigned and exhibits one or more behavioral or location-based risk signals.
    /// ExplainMyPC does NOT claim this is malware — it flags it for user review.
    /// </summary>
    Suspicious         = 4,

    /// <summary>
    /// Unsigned with multiple risk signals (temp location, no description, process anomaly).
    /// Always shown with a confidence caveat. User must decide what to do.
    /// </summary>
    HighRisk           = 5,
}

/// <summary>Confidence level for a security finding.</summary>
public enum FindingConfidence
{
    /// <summary>Observed facts only — no behavioral inference.</summary>
    Observed   = 0,

    /// <summary>Pattern-based inference with high signal-to-noise ratio.</summary>
    Likely     = 1,

    /// <summary>Heuristic pattern — may have false positives.</summary>
    Heuristic  = 2,
}

/// <summary>Severity of a security finding.</summary>
public enum FindingSeverity
{
    Info     = 0,
    Low      = 1,
    Moderate = 2,
    High     = 3,
}

// ── Finding record ────────────────────────────────────────────────────────────

/// <summary>
/// A single security observation surfaced by the security intelligence pipeline.
/// ExplainMyPC uses confidence-aware language — findings are observations, not verdicts.
/// </summary>
public sealed record SecurityFinding(
    string           Id,
    string           Title,
    string           Detail,
    string           Explanation,
    FindingSeverity  Severity,
    FindingConfidence Confidence,
    TrustLevel       TrustLevel,
    string           FilePath,
    string           Publisher,
    DateTimeOffset   DetectedAt)
{
    public string SeverityLabel => Severity switch
    {
        FindingSeverity.High     => "High",
        FindingSeverity.Moderate => "Moderate",
        FindingSeverity.Low      => "Low",
        _                        => "Info",
    };

    public string ConfidenceLabel => Confidence switch
    {
        FindingConfidence.Observed  => "Confirmed observation",
        FindingConfidence.Likely    => "Likely",
        FindingConfidence.Heuristic => "Heuristic — review recommended",
        _                           => string.Empty,
    };

    public string TrustLabel => TrustLevel switch
    {
        TrustLevel.TrustedMicrosoft  => "Microsoft",
        TrustLevel.SignedKnownVendor => "Signed",
        TrustLevel.UnsignedCommon    => "Unsigned (common)",
        TrustLevel.Unsigned          => "Unsigned",
        TrustLevel.Suspicious        => "Suspicious",
        TrustLevel.HighRisk          => "High risk",
        _                            => "Unknown",
    };

    public bool HasFilePath => !string.IsNullOrEmpty(FilePath);
}

// ── Startup trust entry ───────────────────────────────────────────────────────

/// <summary>
/// A startup entry enriched with trust and signature information.
/// </summary>
public sealed record StartupTrustEntry(
    string    Name,
    string    Command,
    string    ExecutablePath,
    string    Publisher,
    TrustLevel TrustLevel,
    bool      IsSigned,
    bool      IsEnabled,
    string    Location,
    string?   RiskReason = null)
{
    public bool HasRisk => TrustLevel >= TrustLevel.Suspicious;
    public string TrustLabel => TrustLevel switch
    {
        TrustLevel.TrustedMicrosoft  => "Microsoft",
        TrustLevel.SignedKnownVendor => "Signed",
        TrustLevel.UnsignedCommon    => "Common app",
        TrustLevel.Unsigned          => "Unsigned",
        TrustLevel.Suspicious        => "Suspicious",
        TrustLevel.HighRisk          => "High risk",
        _                            => "Unknown",
    };
}

// ── Windows security status ───────────────────────────────────────────────────

/// <summary>Status of a single Windows security feature.</summary>
public sealed record SecurityFeatureStatus(
    string FeatureName,
    bool   IsEnabled,
    bool   IsHealthy,
    string StatusDetail)
{
    public string StatusLabel => IsEnabled
        ? (IsHealthy ? "Active" : "Warning")
        : "Disabled";
}

/// <summary>
/// Snapshot of Windows built-in security feature states.
/// All data is read locally — no cloud queries.
/// </summary>
public sealed record WindowsSecurityStatus(
    SecurityFeatureStatus Defender,
    SecurityFeatureStatus Firewall,
    SecurityFeatureStatus SmartScreen,
    SecurityFeatureStatus SecureBoot,
    SecurityFeatureStatus Tpm,
    SecurityFeatureStatus BitLocker,
    SecurityFeatureStatus MemoryIntegrity,
    DateTimeOffset        ReadAt)
{
    public int HealthyCount =>
        new[] { Defender, Firewall, SmartScreen, SecureBoot, Tpm, BitLocker, MemoryIntegrity }
        .Count(f => f.IsEnabled && f.IsHealthy);

    public int TotalCount => 7;

    public IReadOnlyList<SecurityFeatureStatus> All =>
        [Defender, Firewall, SmartScreen, SecureBoot, Tpm, BitLocker, MemoryIntegrity];
}

// ── Security analysis result ──────────────────────────────────────────────────

/// <summary>Complete result of a security intelligence scan.</summary>
public sealed record SecurityAnalysisResult(
    WindowsSecurityStatus           WindowsStatus,
    IReadOnlyList<StartupTrustEntry> StartupEntries,
    IReadOnlyList<SecurityFinding>   Findings,
    int                              PostureScore,
    string                           PostureLabel,
    DateTimeOffset                   CompletedAt,
    TimeSpan                         Duration,
    bool                             WasCancelled)
{
    public int SignedCount   => StartupEntries.Count(e => e.IsSigned);
    public int UnsignedCount => StartupEntries.Count(e => !e.IsSigned);
    public int SuspiciousCount => Findings.Count(f => f.Severity >= FindingSeverity.Moderate);
}
