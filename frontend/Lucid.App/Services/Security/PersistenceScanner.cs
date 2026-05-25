using Lucid.Services.Startup;

namespace Lucid.Services.Security;

/// <summary>
/// Scans persistence locations for suspicious or unsigned entries.
///
/// Scans:
///   1. Startup entries (from StartupSampler data)
///   2. Common suspicious scheduled task patterns
///   3. Non-system services with suspicious properties
///
/// Design principles:
///   • NEVER use the word "malware" or make detection claims.
///   • Always attach a confidence level and explanation.
///   • Surface findings for user review — not for automatic action.
///   • All reads are local; no network, no cloud, no external APIs.
/// </summary>
internal sealed class PersistenceScanner
{
    private readonly SignatureVerificationService _signer;

    // Suspicious path fragments for startup executables
    private static readonly string[] s_suspiciousPaths =
    [
        @"\temp\", @"\tmp\", @"\appdata\local\temp",
        @"\downloads\", @"\recycle", @"\$recycle.bin",
        @"\public\", @"\users\public\",
        @"\programdata\microsoft\windows\start menu\programs\startup",
    ];

    // Suspicious file name patterns
    private static readonly string[] s_suspiciousNamePatterns =
    [
        "update", "helper", "service", "agent", "loader",
        "svc", "host", "mgr", "mon", "sync",
    ];

    // Known common unsigned applications (excluded from suspicion)
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
                    Title:       $"Unsigned startup entry in suspicious location",
                    Detail:      $"{entry.Name} launches from {entry.ExecutablePath}",
                    Explanation: entry.RiskReason ??
                                 "This startup entry runs an unsigned executable from a location " +
                                 "commonly associated with temporary or downloaded files. " +
                                 "This is a pattern worth reviewing — it does not mean the file is harmful.",
                    Severity:    FindingSeverity.High,
                    Confidence:  FindingConfidence.Heuristic,
                    TrustLevel:  entry.TrustLevel,
                    FilePath:    entry.ExecutablePath,
                    Publisher:   entry.Publisher,
                    DetectedAt:  DateTimeOffset.Now));
            }
            else if (entry.TrustLevel == TrustLevel.Suspicious)
            {
                findings.Add(new SecurityFinding(
                    Id:          $"security.startup.suspicious.{seq++}",
                    Title:       $"Unsigned startup entry — {entry.Name}",
                    Detail:      $"Runs unsigned executable: {entry.ExecutablePath}",
                    Explanation: entry.RiskReason ??
                                 "This startup entry is unsigned. Many legitimate applications " +
                                 "do not sign their executables. This is flagged for your awareness, " +
                                 "not as a confirmed threat.",
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

        // Common unsigned app — don't flag
        if (s_commonUnsigned.Contains(entry.ExecutableName))
            return TrustLevel.UnsignedCommon;

        bool inSuspiciousPath = !string.IsNullOrEmpty(exePath) &&
            s_suspiciousPaths.Any(p => exePath.Contains(p, StringComparison.OrdinalIgnoreCase));

        bool hasNoDescription = string.IsNullOrEmpty(entry.Name) ||
            entry.Name.Length < 3;

        bool hasSuspiciousName = s_suspiciousNamePatterns
            .Any(p => entry.ExecutableName.Contains(p, StringComparison.OrdinalIgnoreCase));

        int riskScore = (inSuspiciousPath ? 2 : 0) + (hasNoDescription ? 1 : 0) + (hasSuspiciousName ? 1 : 0);

        if (riskScore >= 2)
        {
            riskReason = inSuspiciousPath
                ? $"This executable runs from {Path.GetDirectoryName(exePath)}, " +
                  "which is a temporary or user-writable path not typical for installed software."
                : "Multiple low-confidence risk signals are present. Review recommended.";
            return riskScore >= 3 ? TrustLevel.HighRisk : TrustLevel.Suspicious;
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
