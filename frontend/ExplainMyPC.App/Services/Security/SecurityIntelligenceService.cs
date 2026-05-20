using ExplainMyPC.Services.ProcessIntel;
using ExplainMyPC.Services.Startup;
using ExplainMyPC.Services.Timeline;
using Microsoft.UI.Dispatching;

namespace ExplainMyPC.Services.Security;

/// <summary>
/// Interface for the security intelligence service.
/// </summary>
public interface ISecurityIntelligenceService
{
    SecurityAnalysisResult? LastResult { get; }
    bool                    IsScanning { get; }
    event EventHandler<SecurityAnalysisResult>? ScanCompleted;
    event EventHandler<int>?                    ScanProgressChanged;
    Task StartScanAsync(CancellationToken ct = default);
    void CancelScan();
}

/// <summary>
/// Orchestrates the security intelligence scan pipeline:
///   1. Windows security feature status (Defender, Firewall, etc.)
///   2. Startup entry trust analysis via signature verification
///   3. Security findings generation from persistence scanner
///   4. Posture score calculation
///   5. Timeline event emission
///
/// Safety contract:
///   â€¢ No cloud API calls.
///   â€¢ No automatic deletion, quarantine, or modification of any files.
///   â€¢ All findings are observations with confidence labels â€” not verdicts.
///   â€¢ Language is always hedged: "may warrant review", not "is malware".
/// </summary>
public sealed class SecurityIntelligenceService : ISecurityIntelligenceService
{
    private readonly DispatcherQueue              _dispatcher;
    private readonly IStartupManagementService    _startup;
    private readonly TimelineAggregationService?  _timeline;
    private readonly SignatureVerificationService _signer = new();

    private CancellationTokenSource? _cts;

    public SecurityAnalysisResult? LastResult { get; private set; }
    public bool                    IsScanning { get; private set; }

    public event EventHandler<SecurityAnalysisResult>? ScanCompleted;
    public event EventHandler<int>?                    ScanProgressChanged;

    public SecurityIntelligenceService(
        DispatcherQueue             dispatcher,
        IStartupManagementService   startup,
        TimelineAggregationService? timeline = null)
    {
        _dispatcher = dispatcher;
        _startup    = startup;
        _timeline   = timeline;
    }

    public async Task StartScanAsync(CancellationToken ct = default)
    {
        if (IsScanning) return;

        _cts      = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsScanning = true;

        EmitTimeline("Security scan started",
            "ExplainMyPC is analyzing startup trust, Windows security features, and persistence.",
            TimelineEventSeverity.Info, isStart: true);

        SecurityAnalysisResult result;
        try
        {
            result = await Task.Run(() => RunScan(_cts.Token), _cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new SecurityAnalysisResult(
                WindowsStatus:  WindowsSecurityStatusReader.Read(),
                StartupEntries: [],
                Findings:       [],
                PostureScore:   0,
                PostureLabel:   "Scan cancelled",
                CompletedAt:    DateTimeOffset.Now,
                Duration:       TimeSpan.Zero,
                WasCancelled:   true);
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }

        if (!result.WasCancelled)
        {
            var highFindings = result.Findings.Where(f => f.Severity >= FindingSeverity.Moderate).ToList();
            string detail =
                $"Posture score: {result.PostureScore}/100. " +
                $"{result.SignedCount} signed, {result.UnsignedCount} unsigned startup entries. " +
                (highFindings.Count > 0
                    ? $"{highFindings.Count} finding(s) warrant review."
                    : "No significant findings.");

            EmitTimeline("Security scan complete", detail,
                highFindings.Count > 0 ? TimelineEventSeverity.Warning : TimelineEventSeverity.Good,
                isStart: false);
        }

        LastResult = result;
        _dispatcher.TryEnqueue(() => ScanCompleted?.Invoke(this, result));
    }

    public void CancelScan() => _cts?.Cancel();

    // â”€â”€ Core scan pipeline â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private SecurityAnalysisResult RunScan(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Phase 1: Windows security status (fast, ~100ms)
        Report(10);
        var winStatus = WindowsSecurityStatusReader.Read();
        if (ct.IsCancellationRequested) return Cancelled(winStatus, sw);

        // Phase 2: Startup entry trust analysis
        Report(25);
        var startupEntries = _startup.GetAllEntries();
        var scanner = new PersistenceScanner(_signer);
        var startupTrust = scanner.AnalyzeStartupEntries(startupEntries, ct);
        if (ct.IsCancellationRequested) return Cancelled(winStatus, sw);

        // Phase 3: Generate findings
        Report(60);
        var findings = new List<SecurityFinding>(scanner.FindingsFromStartup(startupTrust));
        findings.AddRange(FindingsFromWindowsStatus(winStatus));
        findings.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        if (ct.IsCancellationRequested) return Cancelled(winStatus, sw);

        // Phase 4: Posture score
        Report(90);
        var (score, label) = CalculatePosture(winStatus, findings, startupTrust);

        Report(100);
        sw.Stop();

        return new SecurityAnalysisResult(
            WindowsStatus:  winStatus,
            StartupEntries: startupTrust,
            Findings:       findings.AsReadOnly(),
            PostureScore:   score,
            PostureLabel:   label,
            CompletedAt:    DateTimeOffset.Now,
            Duration:       sw.Elapsed,
            WasCancelled:   false);
    }

    // â”€â”€ Findings from Windows security status â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static IEnumerable<SecurityFinding> FindingsFromWindowsStatus(WindowsSecurityStatus status)
    {
        if (!status.Defender.IsEnabled || !status.Defender.IsHealthy)
            yield return new SecurityFinding(
                Id:          "security.defender.disabled",
                Title:       "Windows Defender is not fully active",
                Detail:      status.Defender.StatusDetail,
                Explanation: "Real-time protection helps detect threats before they run. " +
                             "Ensure Defender or a replacement antivirus is active.",
                Severity:    FindingSeverity.High,
                Confidence:  FindingConfidence.Observed,
                TrustLevel:  TrustLevel.Unsigned,
                FilePath:    string.Empty,
                Publisher:   string.Empty,
                DetectedAt:  DateTimeOffset.Now);

        if (!status.Firewall.IsEnabled)
            yield return new SecurityFinding(
                Id:          "security.firewall.disabled",
                Title:       "Windows Firewall is disabled",
                Detail:      status.Firewall.StatusDetail,
                Explanation: "The firewall controls which programs can receive network connections. " +
                             "Disabling it increases exposure to network-based threats.",
                Severity:    FindingSeverity.Moderate,
                Confidence:  FindingConfidence.Observed,
                TrustLevel:  TrustLevel.Unsigned,
                FilePath:    string.Empty,
                Publisher:   string.Empty,
                DetectedAt:  DateTimeOffset.Now);

        if (!status.SmartScreen.IsEnabled)
            yield return new SecurityFinding(
                Id:          "security.smartscreen.disabled",
                Title:       "SmartScreen is disabled",
                Detail:      status.SmartScreen.StatusDetail,
                Explanation: "SmartScreen warns before running unrecognized downloads. " +
                             "Disabling it removes a layer of protection against drive-by downloads.",
                Severity:    FindingSeverity.Low,
                Confidence:  FindingConfidence.Observed,
                TrustLevel:  TrustLevel.Unsigned,
                FilePath:    string.Empty,
                Publisher:   string.Empty,
                DetectedAt:  DateTimeOffset.Now);
    }

    // â”€â”€ Posture score â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static (int Score, string Label) CalculatePosture(
        WindowsSecurityStatus           winStatus,
        List<SecurityFinding>           findings,
        IReadOnlyList<StartupTrustEntry> startup)
    {
        int score = 100;

        // Deduct for disabled Windows security features
        if (!winStatus.Defender.IsEnabled || !winStatus.Defender.IsHealthy) score -= 25;
        if (!winStatus.Firewall.IsEnabled)     score -= 15;
        if (!winStatus.SmartScreen.IsEnabled)  score -= 10;
        if (!winStatus.SecureBoot.IsEnabled)   score -= 5;
        if (!winStatus.Tpm.IsEnabled)          score -= 5;
        if (!winStatus.BitLocker.IsEnabled)    score -= 5;
        if (!winStatus.MemoryIntegrity.IsEnabled) score -= 5;

        // Deduct for findings
        foreach (var f in findings)
        {
            score -= f.Severity switch
            {
                FindingSeverity.High     => 10,
                FindingSeverity.Moderate => 5,
                FindingSeverity.Low      => 2,
                _                        => 0,
            };
        }

        score = Math.Clamp(score, 0, 100);

        string label = score switch
        {
            >= 90 => "Strong posture",
            >= 75 => "Good posture",
            >= 60 => "Fair posture",
            >= 40 => "Needs attention",
            _     => "Review recommended",
        };

        return (score, label);
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private SecurityAnalysisResult Cancelled(WindowsSecurityStatus ws,
        System.Diagnostics.Stopwatch sw) =>
        new(ws, [], [], 0, "Scan cancelled", DateTimeOffset.Now, sw.Elapsed, true);

    private void Report(int pct) =>
        _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, pct));

    private void EmitTimeline(string title, string detail,
        TimelineEventSeverity severity, bool isStart)
    {
        if (_timeline is null) return;
        var ev = new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = isStart ? TimelineEventType.SecurityScanStarted
                                 : TimelineEventType.SecurityScanCompleted,
            OccurredAt = DateTimeOffset.Now,
            Title      = title,
            Detail     = detail,
            Severity   = severity,
        };
        _dispatcher.TryEnqueue(() => _timeline.AddStorageEvent(ev));
    }
}
