using FluentAssertions;
using Lucid.Services.Chat;
using Lucid.Services.Conversation;
using Lucid.Services.Reliability;
using Xunit;

namespace Lucid.Tests.Chat;

/// <summary>
/// What the model is actually told.
///
/// The model does not investigate anything — it explains what the prompt writer
/// hands it. So these tests are the real guard on answer quality, and in
/// particular on the two ways a small model goes wrong when left to itself:
/// concluding a machine is healthy from an absence of data it was never given,
/// and flattening a hedged finding into a confident diagnosis.
/// </summary>
public sealed class ReliabilityPromptWriterTests
{
    private static readonly DateTimeOffset Now =
        new(new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Local));

    private static ReliabilityEvent Event(
        ReliabilityEventKind kind,
        int                  hoursAgo = 1,
        string?              stopCode = null)
        => new()
        {
            Kind         = kind,
            When         = Now.AddHours(-hoursAgo),
            ProviderName = "test",
            EventId      = 41,
            Level        = 1,
            StopCode     = stopCode,
            Summary      = $"{kind} happened",
        };

    private static ReliabilityReport Report(
        IEnumerable<ReliabilityEvent>? events = null,
        bool                           failed = false,
        string?                        reason = null)
    {
        var list = (events ?? []).OrderByDescending(e => e.When).ToList();

        return new ReliabilityReport
        {
            Since             = Now.AddDays(-14),
            GeneratedAt       = Now,
            Events            = list,
            Findings          = failed ? [] : CrashCorrelator.Correlate(list),
            ReadFailed        = failed,
            ReadFailureReason = reason,
        };
    }

    // ── Could not look ────────────────────────────────────────────────────────

    [Fact]
    public void AFailedRead_TellsTheModelNotToClaimTheMachineIsHealthy()
    {
        // This is the case that produced the worst possible answer before: an
        // empty findings list reads as "all clear" to a model unless it is told
        // otherwise, in the exact situation where the user's PC is crashing.
        var prompt = ReliabilityPromptWriter.Write(
            Report(failed: true, reason: "Reading the Windows event log was denied."));

        prompt.Should().Contain("COULD NOT BE READ");
        prompt.Should().Contain("UNKNOWN");
        prompt.Should().Contain("not the same as there being none");
        prompt.Should().Contain("Do NOT say the system looks stable");
    }

    [Fact]
    public void AFailedRead_IncludesTheReasonVerbatim()
        => ReliabilityPromptWriter.Write(Report(failed: true, reason: "Access was denied."))
               .Should().Contain("Access was denied.");

    [Fact]
    public void AFailedRead_WarnsAgainstSubstitutingLiveReadings()
    {
        // The original wrong answer reasoned about a crash from current CPU usage.
        ReliabilityPromptWriter.Write(Report(failed: true, reason: "denied"))
            .Should().Contain("running right now");
    }

    // ── Genuinely quiet ───────────────────────────────────────────────────────

    [Fact]
    public void AQuietMachine_IsDistinguishedFromAFailedRead()
    {
        var prompt = ReliabilityPromptWriter.Write(Report());

        prompt.Should().Contain("genuine absence of events, not a failure to look");
        prompt.Should().NotContain("COULD NOT BE READ");
    }

    [Fact]
    public void AQuietMachine_OffersTheExplanationsThatLeaveNoTrace()
    {
        // A user certain their PC crashed should not be told they are wrong.
        var prompt = ReliabilityPromptWriter.Write(Report());

        prompt.Should().Contain("power cut");
        prompt.Should().Contain("cleared");
        prompt.Should().Contain("Ask when");
    }

    // ── Findings ──────────────────────────────────────────────────────────────

    [Fact]
    public void FindingsCarryTheirConfidenceBand_AndTheModelIsToldToKeepIt()
    {
        var prompt = ReliabilityPromptWriter.Write(Report(
        [
            Event(ReliabilityEventKind.UnexpectedShutdown, 1),
            Event(ReliabilityEventKind.UnexpectedShutdown, 30),
            Event(ReliabilityEventKind.UnexpectedShutdown, 60),
        ]));

        prompt.Should().Contain("HIGH CONFIDENCE");
        prompt.Should().Contain("Keep each finding's confidence level");
        prompt.Should().Contain("Never state a cause as certain");
    }

    [Fact]
    public void LowConfidenceIsLabelledAsWorthReviewingOnly()
    {
        var prompt = ReliabilityPromptWriter.Write(
            Report([Event(ReliabilityEventKind.UnexpectedShutdown)]));

        prompt.Should().Contain("worth reviewing only");
    }

    [Fact]
    public void CountsAndStopCodesAreGivenVerbatim_SoNothingNeedsInventing()
    {
        var prompt = ReliabilityPromptWriter.Write(Report(
        [
            Event(ReliabilityEventKind.BugCheck, 1,  stopCode: "0x00000133"),
            Event(ReliabilityEventKind.BugCheck, 24, stopCode: "0x00000133"),
        ]));

        prompt.Should().Contain("0x00000133");
        prompt.Should().Contain("Seen 2 time(s)");
        prompt.Should().Contain("never invent, round, or estimate");
    }

    [Fact]
    public void StopCodeKnowledgeReachesThePrompt()
    {
        // The model does not need to know what 0x133 is — it is told.
        ReliabilityPromptWriter.Write(Report([Event(ReliabilityEventKind.BugCheck, 1, "0x00000133")]))
            .Should().Contain("DPC_WATCHDOG_VIOLATION");
    }

    [Fact]
    public void SuggestedChecksReachThePrompt()
        => ReliabilityPromptWriter.Write(Report([Event(ReliabilityEventKind.BugCheck, 1, "0x00000124")]))
               .Should().Contain("Worth checking:");

    [Fact]
    public void TheModelIsToldNotToBlameACrashingAppForTheWholeMachine()
    {
        // The specific overreach in the answer that started this work.
        ReliabilityPromptWriter.Write(Report([Event(ReliabilityEventKind.ApplicationCrash)]))
            .Should().Contain("does not explain the whole machine going");
    }

    [Fact]
    public void TheBannedSecurityVocabularyIsRestated()
        => ReliabilityPromptWriter.Write(Report([Event(ReliabilityEventKind.BugCheck)]))
               .Should().Contain("Never use the words malicious, infected, dangerous, or virus");

    [Fact]
    public void EventsAreListedUnderTheFindings()
    {
        var prompt = ReliabilityPromptWriter.Write(
            Report([Event(ReliabilityEventKind.DiskFault)]));

        prompt.Should().Contain("UNDERLYING EVENTS");
        prompt.Should().Contain("DiskFault");
    }

    [Fact]
    public void TheEventListIsCapped_SoOneNoisyMachineCannotFloodTheContext()
    {
        var many = Enumerable.Range(1, 200)
            .Select(i => Event(ReliabilityEventKind.ApplicationCrash, i))
            .ToList();

        var prompt = ReliabilityPromptWriter.Write(Report(many));

        prompt.Should().Contain("older event(s) not listed");
        prompt.Split('\n').Length.Should().BeLessThan(120);
    }

    [Fact]
    public void TheWindowIsStated()
        => ReliabilityPromptWriter.Write(Report()).Should().Contain("14 days");

    // ── The user-facing trail line ────────────────────────────────────────────

    [Fact]
    public void DescribeInvestigation_CountsWhatWasFound()
    {
        var line = ReliabilityPromptWriter.DescribeInvestigation(Report(
        [
            Event(ReliabilityEventKind.UnexpectedShutdown, 1),
            Event(ReliabilityEventKind.UnexpectedShutdown, 20),
            Event(ReliabilityEventKind.BugCheck, 1, "0x00000133"),
        ]));

        line.Should().Contain("2 unexpected shutdowns");
        line.Should().Contain("1 stop error");
        line.Should().Contain("14 days");
    }

    [Fact]
    public void DescribeInvestigation_UsesSingularForOne()
        => ReliabilityPromptWriter.DescribeInvestigation(
               Report([Event(ReliabilityEventKind.UnexpectedShutdown)]))
               .Should().Contain("1 unexpected shutdown")
               .And.NotContain("shutdowns");

    [Fact]
    public void DescribeInvestigation_SaysSoWhenNothingWasFound()
        => ReliabilityPromptWriter.DescribeInvestigation(Report())
               .Should().Contain("no crash, hardware or storage failures recorded");

    [Fact]
    public void DescribeInvestigation_SaysSoWhenItCouldNotLook()
    {
        var line = ReliabilityPromptWriter.DescribeInvestigation(
            Report(failed: true, reason: "Access was denied."));

        line.Should().Contain("could not");
        line.Should().Contain("Access was denied.");
    }
}

/// <summary>
/// Which questions trigger a real investigation. Getting this wrong is either
/// wasteful (reading the event log to answer a question about disk space) or
/// useless (answering a crash question from live telemetry, which is what
/// happened before).
/// </summary>
public sealed class InvestigationPreflightRoutingTests
{
    private static ConversationIntent Resolve(string question) =>
        new ConversationIntentResolver().Resolve(question).Intent;

    [Theory]
    [InlineData("why does my pc keep crashing")]
    [InlineData("my computer crashed again last night")]
    [InlineData("I keep getting blue screens")]
    [InlineData("bsod every time I play a game")]
    [InlineData("it restarts by itself")]
    [InlineData("my pc just shuts down randomly")]
    [InlineData("what does this stop code mean")]
    [InlineData("machine powers off under load")]
    [InlineData("had an unexpected shutdown")]
    public void CrashQuestions_ResolveToTheCrashIntent(string question)
        => Resolve(question).Should().Be(ConversationIntent.WhyDoesItCrash);

    [Fact]
    public void CrashQuestionsAreCheckedBeforeSlownessQuestions()
    {
        // "freeze" and "unresponsive" belong to both vocabularies. A machine that
        // freezes and has to be reset is the more specific — and more urgent —
        // reading, so it must win.
        Resolve("my pc freezes and I have to reboot it")
            .Should().Be(ConversationIntent.WhyDoesItCrash);
    }

    [Theory]
    [InlineData("why is my pc slow")]
    [InlineData("what is using my disk space")]
    [InlineData("is my cpu running hot")]
    [InlineData("what is using my ram")]
    public void NonCrashQuestions_DoNotResolveToTheCrashIntent(string question)
        => Resolve(question).Should().NotBe(ConversationIntent.WhyDoesItCrash);

    [Theory]
    [InlineData(ConversationIntent.WhyDoesItCrash)]
    [InlineData(ConversationIntent.InvestigateProblem)]
    [InlineData(ConversationIntent.WhyDidSomethingChange)]
    public void IntentsThatWarrantReadingTheEventLog(ConversationIntent intent)
        => InvestigationPreflight.NeedsCrashHistory(intent).Should().BeTrue();

    [Theory]
    [InlineData(ConversationIntent.WhyIsDiskFull)]
    [InlineData(ConversationIntent.WhyIsHot)]
    [InlineData(ConversationIntent.Greeting)]
    [InlineData(ConversationIntent.Help)]
    [InlineData(ConversationIntent.OpenStorage)]
    [InlineData(ConversationIntent.Unknown)]
    public void IntentsThatDoNot(ConversationIntent intent)
        => InvestigationPreflight.NeedsCrashHistory(intent).Should().BeFalse();
}
