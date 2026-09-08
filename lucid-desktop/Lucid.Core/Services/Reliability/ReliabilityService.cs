using Lucid.Core.Infrastructure;
using Lucid.Services.Governance;

namespace Lucid.Services.Reliability;

/// <summary>
/// Answers "has this machine been unstable, and what does the evidence point at".
/// </summary>
public interface IReliabilityService
{
    /// <summary>
    /// Reads the event logs over a window and correlates what it finds.
    ///
    /// Never throws for an unreadable log — the returned report carries
    /// <see cref="ReliabilityReport.ReadFailed"/> instead, because a question in
    /// a conversation should get an honest "I could not look" rather than an
    /// exception, and must never get a misleading "nothing found".
    /// </summary>
    Task<ReliabilityReport> InvestigateAsync(TimeSpan? window = null, CancellationToken ct = default);
}

/// <summary>
/// Orchestrates the reliability investigation: read the logs, classify, correlate.
///
/// The three steps are separate on purpose — reading is I/O behind an interface,
/// while classification and correlation are pure and tested. This class only
/// handles the parts that need a running system: governance, bounding, caching
/// and failure handling.
///
/// Governance: classified <see cref="WorkloadCategory.ReliabilityAnalysis"/>,
/// which is Foreground because it only runs when the user has just asked
/// something. Foreground does not mean unbounded: the record caps below are the
/// real protection, since an event-log query on a long-lived machine is the kind
/// of operation that could otherwise take seconds and pull megabytes.
///
/// Caching: a conversation asks about crashes several times in a row — the
/// follow-up questions are the whole point of chat. Re-reading the log for each
/// one would be wasteful, and the answer cannot have changed in the meantime, so
/// reports are reused briefly.
/// </summary>
public sealed class ReliabilityService : IReliabilityService
{
    /// <summary>
    /// Default lookback. Long enough to establish whether something is a pattern,
    /// short enough that a machine's ancient history does not drown the signal.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(14);

    private const int MaxSystemRecords      = 400;
    private const int MaxApplicationRecords = 300;

    /// <summary>
    /// How long a report stays fresh. Short enough that a crash during the
    /// conversation is not missed for long, long enough to absorb the follow-up
    /// questions that immediately follow the first one.
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);

    private readonly IWindowsEventLogReader     _reader;
    private readonly IRuntimeGovernanceService? _governance;
    private readonly ILucidLogger?              _logger;
    private readonly Func<DateTimeOffset>       _clock;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private ReliabilityReport? _cached;
    private TimeSpan           _cachedWindow;
    private DateTimeOffset     _cachedAt;

    public ReliabilityService(
        IWindowsEventLogReader     reader,
        IRuntimeGovernanceService? governance = null,
        ILucidLogger?              logger     = null,
        Func<DateTimeOffset>?      clock      = null)
    {
        _reader     = reader;
        _governance = governance;
        _logger     = logger;
        _clock      = clock ?? (() => DateTimeOffset.Now);
    }

    // ── Investigation ─────────────────────────────────────────────────────────

    public async Task<ReliabilityReport> InvestigateAsync(
        TimeSpan?         window = null,
        CancellationToken ct     = default)
    {
        var effectiveWindow = window ?? DefaultWindow;

        // Serialised: two questions arriving together should share one read
        // rather than race each other into the event log.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _clock();

            if (_cached is not null &&
                _cachedWindow == effectiveWindow &&
                now - _cachedAt < CacheLifetime)
                return _cached;

            var report = await RunAsync(effectiveWindow, now, ct).ConfigureAwait(false);

            // Only cache successful reads. Caching a failure would keep reporting
            // "could not look" for two minutes after a transient problem cleared.
            if (!report.ReadFailed)
            {
                _cached       = report;
                _cachedWindow = effectiveWindow;
                _cachedAt     = now;
            }

            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ReliabilityReport> RunAsync(
        TimeSpan          window,
        DateTimeOffset    now,
        CancellationToken ct)
    {
        var since = now - window;

        const string workloadName = "Reliability investigation";
        var          acquired     = false;

        if (_governance is not null)
        {
            acquired = _governance.TryAcquireSlot(
                WorkloadCategory.ReliabilityAnalysis, workloadName, out var refusal);

            // A refusal means another investigation is already in flight — the
            // per-category limit is 1. Foreground work is never deferred, and
            // the answer would be identical, so proceed and note it.
            if (!acquired)
                _logger?.Info("Reliability",
                    $"Proceeding without a governance slot: {refusal ?? "slot unavailable"}");
        }

        try
        {
            var system = await ReadLogAsync(
                "System", ReliabilityEventClassifier.SystemQuery,
                since, MaxSystemRecords, ct).ConfigureAwait(false);

            var application = await ReadLogAsync(
                "Application", ReliabilityEventClassifier.ApplicationQuery,
                since, MaxApplicationRecords, ct).ConfigureAwait(false);

            // Only a *total* failure counts as "could not look". If either log
            // was readable we have something real to say, and the crash history
            // that matters most lives in the System log.
            if (system.Failure is not null && application.Failure is not null)
            {
                _logger?.Warning("Reliability",
                    $"Event log investigation failed: {system.Failure.Message}", system.Failure);

                return new ReliabilityReport
                {
                    Since             = since,
                    GeneratedAt       = now,
                    ReadFailed        = true,
                    ReadFailureReason = DescribeFailure(system.Failure),
                };
            }

            var events = ReliabilityEventClassifier
                .ClassifyAll(system.Records.Concat(application.Records))
                .OrderByDescending(e => e.When)
                .ToList();

            return new ReliabilityReport
            {
                Since       = since,
                GeneratedAt = now,
                Events      = events,
                Findings    = CrashCorrelator.Correlate(events),
            };
        }
        finally
        {
            if (acquired)
                _governance?.ReleaseSlot(WorkloadCategory.ReliabilityAnalysis, workloadName);
        }
    }

    /// <summary>Outcome of reading one log: what came back, or why nothing did.</summary>
    private readonly record struct LogReadResult(
        IReadOnlyList<RawEventRecord> Records,
        Exception?                    Failure);

    /// <summary>
    /// Reads one log, reporting rather than throwing on failure.
    ///
    /// Per-log tolerance is deliberate: the Application log being unreadable
    /// should not cost us the System log's crash history, which is where the
    /// answer usually is. The failure is returned rather than swallowed, so the
    /// caller can tell a partial failure from a total one — the difference
    /// between "here is what I found" and "I could not look at all".
    /// </summary>
    private async Task<LogReadResult> ReadLogAsync(
        string                        logName,
        IReadOnlyList<EventQuerySpec> specs,
        DateTimeOffset                since,
        int                           maxRecords,
        CancellationToken             ct)
    {
        try
        {
            var records = await _reader
                .ReadAsync(logName, specs, since, maxRecords, ct)
                .ConfigureAwait(false);

            return new LogReadResult(records, null);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the user's doing, not a read failure. Let it out.
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Warning("Reliability", $"Could not read the {logName} log: {ex.Message}", ex);
            return new LogReadResult([], ex);
        }
    }

    /// <summary>
    /// Plain-English failure text, shown to the user as-is. Says what could not
    /// be done and why, without implying the machine is fine.
    /// </summary>
    private static string DescribeFailure(Exception ex) => ex switch
    {
        UnauthorizedAccessException =>
            "Reading the Windows event log was denied. Running Lucid as administrator would allow it.",
        _ =>
            $"The Windows event log could not be read ({ex.GetType().Name}). " +
            "Crash history is unavailable, which is not the same as there being none.",
    };
}
