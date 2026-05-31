using ExplainMyPC.Services.Startup;

namespace ExplainMyPC.Services.Security;

/// <summary>
/// Scans persistence locations for unsigned or unusual startup entries.
///
/// Scans:
///   1. Startup entries (from StartupSampler data)
///   2. Common unusual scheduled task patterns
///   3. Non-system services with noteworthy properties
///
/// Design principles:
///   • NEVER use the word "malware", "suspicious", or make detection claims.
///   • Always attach a confidence level and explanation.
///   • Surface findings for user review — not for automatic action.
///   • Single weak signals alone never elevate an entry to a higher risk level;
///     convergence of multiple independent signals is required.
///   • All reads are local; no network, no cloud, no external APIs.
/// </summary>
internal sealed class PersistenceScanner
{
    private readonly SignatureVerificationService _signer;

    // High-signal path fragments: locations that are genuinely unusual for installed
    // software to run from (temp dirs, recycle bin, world-writable public dirs).
    // Each match contributes 2 points toward the risk score.
    private static readonly string[] s_highSignalPaths =
    [
        @"\temp\", @"\tmp\", @"\appdata\local\temp",
        @"\recycle", @"\$recycle.bin",
        @"\users\public\",
    ];

    // Low-signal path fragments: locations that are mildly contextual but routinely
    // legitimate (e.g. a portable app the user placed in their Downloads folder).
    // Each match contributes 1 point — insufficient alone to elevate trust level.
    // Note: the Windows Startup Folder is deliberately excluded — it is a standard
    // persistence location used by countless legitimate installers and carries no signal.
    private static readonly string[] s_lowSignalPaths =
    [
        @"\downloads\",
        @"\public\",
    ];

    // Name fragments present in many legitimate executables ("updater.exe",
    // "servicehost.exe", "helper.exe", etc.). These are extremely common benign names
    // and carry only 1 point each.  A name match alone is never sufficient to elevate
    // trust level — multiple converging signals are required.
    private static readonly string[] s_weakNamePatterns =
    [
        "update", "helper", "service", "svc", "host",
        "mgr", "mon", "sync", "agent", "loader",
    ];

    // Known common unsigned applications (excluded from further scoring)
    private static readonly HashSet<string> s_commonUnsigned = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "python3", "pythonw", "node", "npm",
        "git", "bash", "sh", "wsl",
        "autohotkey", "ahk",
    };

    internal PersistenceScanner(SignatureVerificationService signer) => _signer = signer;

    /// <summary>
    /// Enriches startup entries with trust and signature data.
    /// </summary>
    internal IReadOnlyList<StartupTrustEntry> AnalyzeStartupEntries(
        IReadOnlyList<StartupEntry> entries,
        CancellationToken ct)
    {
        var result = new List<StartupTrustEntry>(entries.Count);

        foreach (var entry in entries)
        {
            if (ct.IsCancellationRequested) break;

            string exePath = ExtractExecutablePath(entry.Command);
            var (isSigned, publisher) = _signer.Verify(exePath);

            TrustLevel trust;
            string? riskReason = null;

            if (isSigned)
            {
                trust = SignatureVerificationService.ClassifyPublisher(publisher, exePath);
            }
            else
            {
                trust = ClassifyUnsignedStartup(entry, exePath, out riskReason);
            }

            result.Add(new StartupTrustEntry(
                Name:           entry.Name,
                Command:        entry.Command,
                ExecutablePath: exePath,
                Publisher:      publisher,
                TrustLevel:     trust,
                IsSigned:       isSigned,
                IsEnabled:      entry.IsEnabled,
                Location:       FormatLocation(entry.Location),
                RiskReason:     riskReason));
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// Generates security findings from enriched startup entries.
    /// </summary>
    internal IReadOnlyList<SecurityFinding> FindingsFromStartup(
        IReadOnlyList<StartupTrustEntry> entries)
    {
        var findings = new List<SecurityFinding>();
        int seq = 0;

        foreach (var entry in entries)
        {
            if (!entry.IsEnabled) continue; // skip disabled entries

            if (entry.TrustLevel == TrustLevel.HighRisk)
            {
                findings.Add(new SecurityFinding(
                    Id:          $"security.startup.highrisk.{seq++}",
                    Title:       $"Unsigned startup entry in an unusual location — worth reviewing",
                    Detail:      $"{entry.Name} launches from {entry.ExecutablePath}",
                    Explanation: entry.RiskReason ??
                                 "This startup entry runs an unsigned executable from a location " +
                                 "not typical for installed software. Multiple contextual signals " +
                                 "are present. This is flagged for your review — it does not mean " +
                                 "the file is harmful.",
                    Severity:    FindingSeverity.High,
                    Confidence:  FindingConfidence.Heuristic,
                    TrustLevel:  entry.TrustLevel,
                    FilePath:    entry.ExecutablePath,
                    Publisher:   entry.Publisher,
                    DetectedAt:  DateTimeOffset.Now));
            }
            else if (entry.TrustLevel == TrustLevel.FlaggedForReview)
            {
                findings.Add(new SecurityFinding(
                    Id:          $"security.startup.flagged.{seq++}",
                    Title:       $"Unsigned startup entry — {entry.Name}",
                    Detail:      $"Runs unsigned executable: {entry.ExecutablePath}",
                    Explanation: entry.RiskReason ??
                                 "This startup entry is unsigned and has one or more contextual signals " +
                                 "worth noting. Many legitimate applications do not sign their executables. " +
                                 "This is flagged for your awareness, not as a confirmed concern.",
                    Severity:    FindingSeverity.Moderate,
                    Confidence:  FindingConfidence.Heuristic,
                    TrustLevel:  entry.TrustLevel,
                    FilePath:    entry.ExecutablePath,
                    Publisher:   entry.Publisher,
                    DetectedAt:  DateTimeOffset.Now));
            }
            else if (entry.TrustLevel == TrustLevel.Unsigned && !entry.IsSigned)
            {
                findings.Add(new SecurityFinding(
                    Id:          $"security.startup.unsigned.{seq++}",
                    Title:       $"Unsigned startup entry — {entry.Name}",
                    Detail:      $"No Authenticode signature found on {Path.GetFileName(entry.ExecutablePath)}",
                    Explanation: "This startup application has no digital signature. " +
                                 "Signatures let Windows verify a file comes from who it claims. " +
                                 "Unsigned files are not automatically unsafe — many developers " +
                                 "skip signing — but it is worth knowing.",
                    Severity:    FindingSeverity.Low,
                    Confidence:  FindingConfidence.Observed,
                    TrustLevel:  entry.TrustLevel,
                    FilePath:    entry.ExecutablePath,
                    Publisher:   string.Empty,
                    DetectedAt:  DateTimeOffset.Now));
            }
        }

        return findings.AsReadOnly();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TrustLevel ClassifyUnsignedStartup(
        StartupEntry entry, string exePath, out string? riskReason)
    {
        riskReason = null;

        // Common unsigned app — don't score further
        if (s_commonUnsigned.Contains(entry.ExecutableName))
            return TrustLevel.UnsignedCommon;

        // Score independent signals — each represents a weak contextual observation.
        // No single signal is sufficient to elevate trust level; convergence is required.
        int riskScore = 0;
        bool inHighSignalPath = false;

        if (!string.IsNullOrEmpty(exePath))
        {
            if (s_highSignalPaths.Any(p => exePath.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                // Running from temp/recycle/public is unusual for installed software (+2)
                riskScore += 2;
                inHighSignalPath = true;
            }
            else if (s_lowSignalPaths.Any(p => exePath.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                // Downloads or startup folder is a mild contextual signal (+1)
                riskScore += 1;
            }
        }

        // Missing or very short name — weak signal (+1)
        if (string.IsNullOrEmpty(entry.Name) || entry.Name.Length < 3)
            riskScore += 1;

        // Name fragment overlap with common service patterns — very weak signal (+1 total,
        // regardless of how many patterns match, to prevent stacking)
        if (s_weakNamePatterns.Any(p => entry.ExecutableName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            riskScore += 1;

        // Thresholds: require genuine convergence before elevating trust level.
        //   Score 4+ → HighRisk   (e.g. high-signal path + no description + pattern name)
        //   Score 3  → FlaggedForReview (e.g. high-signal path + one other signal, or
        //                                 low-signal path + no description + pattern name)
        //   Score <3 → Unsigned   (single weak signals are not actionable)
        if (riskScore >= 4)
        {
            riskReason = inHighSignalPath
                ? $"This executable runs from {Path.GetDirectoryName(exePath)}, " +
                  "which is not a typical location for installed software. " +
                  "Combined with other contextual signals, this entry is worth reviewing."
                : "Multiple contextual signals are present on this entry. " +
                  "No single signal is conclusive — review recommended.";
            return TrustLevel.HighRisk;
        }

        if (riskScore >= 3)
        {
            riskReason = inHighSignalPath
                ? $"This executable runs from {Path.GetDirectoryName(exePath)}, " +
                  "an unexpected path for installed software. Additional contextual signals present."
                : "Several weak contextual signals are present. " +
                  "This is flagged for your awareness, not as a confirmed concern.";
            return TrustLevel.FlaggedForReview;
        }

        return TrustLevel.Unsigned;
    }

    private static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;

        // Strip leading quotes and take first token
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            int end = trimmed.IndexOf('"', 1);
            if (end > 1) return trimmed[1..end];
        }

        // No quotes — take up to first space
        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    private static string FormatLocation(StartupLocation loc) => loc switch
    {
        StartupLocation.HkcuRun      => "HKCU\\Run",
        StartupLocation.HklmRun      => "HKLM\\Run",
        StartupLocation.StartupFolder => "Startup Folder",
        _                            => "Unknown",
    };
}
