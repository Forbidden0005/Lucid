using FluentAssertions;
using Lucid.Services.Reliability;
using Xunit;

namespace Lucid.Tests.Reliability;

/// <summary>
/// The orchestration layer: bounding, caching, and — most importantly — never
/// reporting an unreadable log as a healthy machine.
/// </summary>
public sealed class ReliabilityServiceTests
{
    private static readonly DateTimeOffset Now =
        new(new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Local));

    // ── Fake reader ───────────────────────────────────────────────────────────

    private sealed class FakeReader : IWindowsEventLogReader
    {
        private readonly Dictionary<string, List<RawEventRecord>> _byLog;
        private readonly Exception?                               _throwFor;
        private readonly string?                                  _throwingLog;

        public int  CallCount    { get; private set; }
        public List<(string Log, int Max, DateTimeOffset Since)> Calls { get; } = [];

        public FakeReader(
            Dictionary<string, List<RawEventRecord>>? byLog       = null,
            Exception?                                throwFor    = null,
            string?                                   throwingLog = null)
        {
            _byLog       = byLog ?? [];
            _throwFor    = throwFor;
            _throwingLog = throwingLog;
        }

        public Task<IReadOnlyList<RawEventRecord>> ReadAsync(
            string                        logName,
            IReadOnlyList<EventQuerySpec> specs,
            DateTimeOffset                since,
            int                           maxRecords,
            CancellationToken             ct = default)
        {
            CallCount++;
            Calls.Add((logName, maxRecords, since));

            if (_throwFor is not null && (_throwingLog is null || _throwingLog == logName))
                throw _throwFor;

            IReadOnlyList<RawEventRecord> records =
                _byLog.TryGetValue(logName, out var list) ? list : [];

            return Task.FromResult(records);
        }
    }

    private static RawEventRecord Shutdown(int hoursAgo) => new()
    {
        LogName      = "System",
        ProviderName = "Microsoft-Windows-Kernel-Power",
        EventId      = 41,
        Level        = 1,
        TimeCreated  = Now.AddHours(-hoursAgo),
    };

    private static ReliabilityService Build(FakeReader reader) =>
        new(reader, governance: null, logger: null, clock: () => Now);

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvestigateAsync_ClassifiesAndCorrelatesWhatItReads()
    {
        var reader = new FakeReader(new()
        {
            ["System"] = [Shutdown(1), Shutdown(26), Shutdown(50)],
        });

        var report = await Build(reader).InvestigateAsync();

        report.ReadFailed.Should().BeFalse();
        report.Events.Should().HaveCount(3);
        report.Findings.Should().ContainSingle(f => f.Headline.Contains("unexpected shutdown"));
        report.Findings[0].Confidence.Should().Be(FindingConfidence.High);
        report.GeneratedAt.Should().Be(Now);
    }

    [Fact]
    public async Task InvestigateAsync_ReadsBothTheSystemAndApplicationLogs()
    {
        var reader = new FakeReader();

        await Build(reader).InvestigateAsync();

        reader.Calls.Select(c => c.Log).Should().BeEquivalentTo(["System", "Application"]);
    }

    [Fact]
    public async Task InvestigateAsync_BoundsEveryRead()
    {
        // Unbounded event-log reads are the failure mode that would make Lucid
        // the reason the PC is slow.
        var reader = new FakeReader();

        await Build(reader).InvestigateAsync();

        reader.Calls.Should().OnlyContain(c => c.Max > 0 && c.Max <= 500);
    }

    [Fact]
    public async Task InvestigateAsync_UsesTheRequestedWindow()
    {
        var reader = new FakeReader();

        await Build(reader).InvestigateAsync(TimeSpan.FromDays(3));

        reader.Calls.Should().OnlyContain(c => c.Since == Now.AddDays(-3));
    }

    [Fact]
    public async Task InvestigateAsync_DefaultsToATwoWeekWindow()
    {
        var reader = new FakeReader();

        await Build(reader).InvestigateAsync();

        reader.Calls.Should().OnlyContain(c => c.Since == Now - ReliabilityService.DefaultWindow);
    }

    [Fact]
    public async Task InvestigateAsync_EventsAreNewestFirst()
    {
        var reader = new FakeReader(new()
        {
            ["System"] = [Shutdown(50), Shutdown(1), Shutdown(20)],
        });

        var report = await Build(reader).InvestigateAsync();

        report.Events.Select(e => e.When).Should().BeInDescendingOrder();
    }

    // ── A quiet machine ───────────────────────────────────────────────────────

    [Fact]
    public async Task InvestigateAsync_NothingLogged_IsCleanRatherThanFailed()
    {
        var report = await Build(new FakeReader()).InvestigateAsync();

        report.ReadFailed.Should().BeFalse();
        report.IsClean.Should().BeTrue();
        report.Findings.Should().BeEmpty();
    }

    // ── Failure handling ──────────────────────────────────────────────────────

    [Fact]
    public async Task InvestigateAsync_AccessDenied_ReportsAFailedRead_NotAHealthyMachine()
    {
        // The distinction this test protects is the whole point: telling a user
        // with a crashing PC that nothing is wrong, because we could not look,
        // would be worse than saying nothing.
        var reader = new FakeReader(throwFor: new UnauthorizedAccessException("denied"));

        var report = await Build(reader).InvestigateAsync();

        report.ReadFailed.Should().BeTrue();
        report.IsClean.Should().BeFalse();
        report.ReadFailureReason.Should().Contain("administrator");
    }

    [Fact]
    public async Task InvestigateAsync_UnexpectedFailure_SaysSoWithoutImplyingHealth()
    {
        var reader = new FakeReader(throwFor: new InvalidOperationException("broken"));

        var report = await Build(reader).InvestigateAsync();

        report.ReadFailed.Should().BeTrue();
        report.ReadFailureReason.Should().Contain("not the same as there being none");
    }

    [Fact]
    public async Task InvestigateAsync_OneLogFailing_DoesNotCostUsTheOther()
    {
        // The crash history lives in the System log. The Application log being
        // unreadable must not throw the answer away.
        var reader = new FakeReader(
            byLog: new() { ["System"] = [Shutdown(1), Shutdown(20), Shutdown(40)] },
            throwFor: new UnauthorizedAccessException("denied"),
            throwingLog: "Application");

        var report = await Build(reader).InvestigateAsync();

        report.ReadFailed.Should().BeFalse();
        report.Events.Should().HaveCount(3);
        report.Findings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvestigateAsync_Cancellation_Propagates()
    {
        var reader = new FakeReader(throwFor: new OperationCanceledException());

        var act = async () => await Build(reader).InvestigateAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Caching ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvestigateAsync_RepeatedQuestions_ReuseOneRead()
    {
        // A conversation asks follow-ups. Re-reading the log for each one is
        // wasted work, and the answer cannot have changed within the window.
        var reader  = new FakeReader();
        var service = Build(reader);

        await service.InvestigateAsync();
        await service.InvestigateAsync();
        await service.InvestigateAsync();

        reader.CallCount.Should().Be(2);   // System + Application, once
    }

    [Fact]
    public async Task InvestigateAsync_ADifferentWindow_IsNotServedFromCache()
    {
        var reader  = new FakeReader();
        var service = Build(reader);

        await service.InvestigateAsync(TimeSpan.FromDays(14));
        await service.InvestigateAsync(TimeSpan.FromDays(1));

        reader.CallCount.Should().Be(4);
    }

    [Fact]
    public async Task InvestigateAsync_AFailedReadIsNotCached()
    {
        // Caching a failure would keep reporting "could not look" for minutes
        // after a transient problem cleared.
        var reader  = new FakeReader(throwFor: new InvalidOperationException("transient"));
        var service = Build(reader);

        await service.InvestigateAsync();
        await service.InvestigateAsync();

        reader.CallCount.Should().Be(4);   // retried rather than reused
    }

    [Fact]
    public async Task InvestigateAsync_CacheExpires()
    {
        var clock   = Now;
        var reader  = new FakeReader();
        var service = new ReliabilityService(reader, null, null, () => clock);

        await service.InvestigateAsync();
        clock = Now.AddMinutes(5);
        await service.InvestigateAsync();

        reader.CallCount.Should().Be(4);
    }
}

/// <summary>
/// The event-log query itself. Building this wrong is quiet and expensive: a
/// malformed clause silently matches the wrong events, or matches everything.
/// </summary>
public sealed class WindowsEventLogQueryTests
{
    private static readonly DateTimeOffset Since =
        new(2026, 8, 1, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void BuildXPath_NarrowsByProviderAndEventId()
    {
        var xpath = WindowsEventLogReader.BuildXPath(
            [new EventQuerySpec { ProviderName = "Microsoft-Windows-Kernel-Power", EventIds = [41] }],
            Since);

        xpath.Should().Contain("Provider[@Name='Microsoft-Windows-Kernel-Power']");
        xpath.Should().Contain("EventID=41");
    }

    [Fact]
    public void BuildXPath_OmitsTheIdFilterWhenEveryEventIsWanted()
    {
        var xpath = WindowsEventLogReader.BuildXPath(
            [new EventQuerySpec { ProviderName = "Microsoft-Windows-WHEA-Logger" }],
            Since);

        xpath.Should().Contain("Provider[@Name='Microsoft-Windows-WHEA-Logger']");
        xpath.Should().NotContain("EventID");
    }

    [Fact]
    public void BuildXPath_CombinesSpecsWithOr_SoOnePassCoversEverything()
    {
        var xpath = WindowsEventLogReader.BuildXPath(
        [
            new EventQuerySpec { ProviderName = "BugCheck" },
            new EventQuerySpec { ProviderName = "disk", EventIds = [7, 51] },
        ], Since);

        xpath.Should().Contain(" or ");
        xpath.Should().Contain("'BugCheck'").And.Contain("'disk'");
    }

    [Fact]
    public void BuildXPath_ConstrainsTheTimeWindowInUtc()
    {
        var xpath = WindowsEventLogReader.BuildXPath(
            [new EventQuerySpec { ProviderName = "BugCheck" }], Since);

        xpath.Should().Contain("TimeCreated[@SystemTime>='2026-08-01T09:30:00.000Z']");
    }

    [Fact]
    public void BuildXPath_ConvertsANonUtcWindowStart()
    {
        var offsetTime = new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.FromHours(-7));

        WindowsEventLogReader.BuildXPath(
            [new EventQuerySpec { ProviderName = "BugCheck" }], offsetTime)
            .Should().Contain("2026-08-01T16:30:00.000Z");
    }

    [Fact]
    public void BuildXPath_SkipsProviderNamesThatWouldCorruptTheQuery()
    {
        // Names come from a static table, never user input — but a stray quote
        // would turn the query into something that matches the wrong events
        // rather than failing loudly, so it is rejected.
        var xpath = WindowsEventLogReader.BuildXPath(
        [
            new EventQuerySpec { ProviderName = "Bad'Name" },
            new EventQuerySpec { ProviderName = "BugCheck" },
        ], Since);

        xpath.Should().NotContain("Bad'Name");
        xpath.Should().Contain("'BugCheck'");
    }

    [Fact]
    public void BuildXPath_NoUsableSpecs_ReturnsNullRatherThanMatchingEverything()
    {
        // A malformed spec list must not silently widen into "read the whole log".
        // Null tells the reader to return nothing, which is the safe direction to
        // fail in for an operation whose entire purpose is staying bounded.
        WindowsEventLogReader.BuildXPath(
            [new EventQuerySpec { ProviderName = "  " }], Since).Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_NoUsableSpecs_ReadsNothing()
    {
        var records = await new WindowsEventLogReader().ReadAsync(
            "System", [new EventQuerySpec { ProviderName = "  " }], Since, 100);

        records.Should().BeEmpty();
    }

    [Fact]
    public void EveryConfiguredProviderNameIsQuerySafe()
    {
        var all = ReliabilityEventClassifier.SystemQuery
            .Concat(ReliabilityEventClassifier.ApplicationQuery)
            .ToList();

        all.Should().OnlyContain(s =>
            !string.IsNullOrWhiteSpace(s.ProviderName) &&
            !s.ProviderName.Contains('\'') &&
            !s.ProviderName.Contains('"'));
    }
}
