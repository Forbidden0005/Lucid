using FluentAssertions;
using Lucid.Services.Reliability;
using Xunit;

namespace Lucid.Tests.Reliability;

/// <summary>
/// The reasoning step: turning a pile of events into ranked explanations.
///
/// The behaviour these tests pin down is the difference between Lucid saying
/// something useful and Lucid restating the log back at the user — particularly
/// that one occurrence is not a pattern, and that corroborated evidence earns
/// higher confidence than a lone event.
/// </summary>
public sealed class CrashCorrelatorTests
{
    private static readonly DateTimeOffset Now =
        new(new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Local));

    private static ReliabilityEvent Event(
        ReliabilityEventKind kind,
        int                  hoursAgo    = 1,
        string?              stopCode    = null,
        string?              component   = null,
        string?              processName = null,
        byte                 level       = 2)
        => new()
        {
            Kind         = kind,
            When         = Now.AddHours(-hoursAgo),
            ProviderName = "test",
            EventId      = 1,
            Level        = level,
            StopCode     = stopCode,
            Component    = component,
            ProcessName  = processName,
            Summary      = $"{kind} event",
        };

    // ── Nothing to say ────────────────────────────────────────────────────────

    [Fact]
    public void NoEvents_ProducesNoFindings()
        => CrashCorrelator.Correlate([]).Should().BeEmpty();

    // ── Unexpected shutdowns ──────────────────────────────────────────────────

    [Fact]
    public void OneShutdown_WithNothingElse_IsLowConfidence()
    {
        // A single unexplained restart is genuinely weak evidence. Reporting it
        // as a diagnosis would be the antivirus-style overreach the project rules out.
        var findings = CrashCorrelator.Correlate([Event(ReliabilityEventKind.UnexpectedShutdown)]);

        findings.Should().ContainSingle();
        findings[0].Confidence.Should().Be(FindingConfidence.Low);
        findings[0].Occurrences.Should().Be(1);
        findings[0].Headline.Should().Be("One unexpected shutdown");
    }

    [Fact]
    public void ThreeShutdowns_IsAPattern_AndHighConfidence()
    {
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 1),
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 30),
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 70),
        ]);

        var shutdowns = findings.Single(f => f.Headline.Contains("unexpected shutdown"));
        shutdowns.Confidence.Should().Be(FindingConfidence.High);
        shutdowns.Occurrences.Should().Be(3);
    }

    [Fact]
    public void ShutdownsWithoutStopErrors_PointAtPowerAndHeat()
    {
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 1),
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 20),
        ]);

        var shutdowns = findings.Single(f => f.Headline.Contains("unexpected shutdown"));
        shutdowns.Explanation.Should().Contain("power");
        shutdowns.SuggestedChecks.Should().Contain(c => c.Contains("temperature"));
    }

    [Fact]
    public void ShutdownsAlongsideAStopError_TellADifferentStory()
    {
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 1),
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 20),
            Event(ReliabilityEventKind.BugCheck, hoursAgo: 1, stopCode: "0x00000133"),
        ]);

        var shutdowns = findings.Single(f => f.Headline.Contains("unexpected shutdown"));
        shutdowns.Confidence.Should().Be(FindingConfidence.High);
        shutdowns.Explanation.Should().Contain("stop error");
    }

    // ── Stop errors ───────────────────────────────────────────────────────────

    [Fact]
    public void AKnownStopCode_ExplainsItselfEvenFromOneOccurrence()
    {
        // The code is a documented fact, so a single occurrence already supports
        // a real explanation — unlike a bare count.
        var findings = CrashCorrelator.Correlate(
            [Event(ReliabilityEventKind.BugCheck, stopCode: "0x00000133")]);

        var bugCheck = findings.Single(f => f.Headline.Contains("0x00000133"));
        bugCheck.Confidence.Should().Be(FindingConfidence.Moderate);
        bugCheck.Explanation.Should().Contain("DPC_WATCHDOG_VIOLATION");
        bugCheck.SuggestedChecks.Should().Contain(c => c.Contains("firmware"));
    }

    [Fact]
    public void TheSameStopCodeRepeating_IsGroupedIntoOneFinding()
    {
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.BugCheck, hoursAgo: 1,  stopCode: "0x00000133"),
            Event(ReliabilityEventKind.BugCheck, hoursAgo: 24, stopCode: "0x00000133"),
            Event(ReliabilityEventKind.BugCheck, hoursAgo: 48, stopCode: "0x00000133"),
        ]);

        var bugChecks = findings.Where(f => f.Headline.Contains("0x00000133")).ToList();
        bugChecks.Should().ContainSingle();
        bugChecks[0].Occurrences.Should().Be(3);
        bugChecks[0].Confidence.Should().Be(FindingConfidence.High);
    }

    [Fact]
    public void SeveralDifferentStopCodes_RaiseTheirOwnFinding()
    {
        // Varying codes point at something shared — memory, storage, power —
        // rather than at one misbehaving driver.
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.BugCheck, hoursAgo: 1,  stopCode: "0x0000000A"),
            Event(ReliabilityEventKind.BugCheck, hoursAgo: 20, stopCode: "0x00000050"),
            Event(ReliabilityEventKind.BugCheck, hoursAgo: 40, stopCode: "0x0000001A"),
        ]);

        var varied = findings.Single(f => f.Headline.Contains("different stop codes"));
        varied.Explanation.Should().Contain("memory");
        varied.SuggestedChecks.Should().Contain(c => c.Contains("MemTest86"));
    }

    [Fact]
    public void AnUnrecognisedStopCode_PointsAtTheDumpRatherThanGuessing()
    {
        var findings = CrashCorrelator.Correlate(
            [Event(ReliabilityEventKind.BugCheck, stopCode: "0x00000ABC")]);

        var bugCheck = findings.Single(f => f.Headline.Contains("0x00000ABC"));
        bugCheck.Confidence.Should().Be(FindingConfidence.Low);
        bugCheck.SuggestedChecks.Should().Contain(c => c.Contains("Minidump"));
    }

    // ── Hardware ──────────────────────────────────────────────────────────────

    [Fact]
    public void WheaErrors_AreModerateAlone_ButHighWhenA124Confirms()
    {
        var alone = CrashCorrelator.Correlate([Event(ReliabilityEventKind.HardwareError)]);
        alone.Single(f => f.Headline.Contains("Hardware-level"))
             .Confidence.Should().Be(FindingConfidence.Moderate);

        var corroborated = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.HardwareError),
            Event(ReliabilityEventKind.BugCheck, stopCode: "0x00000124"),
        ]);

        var hardware = corroborated.Single(f => f.Headline.Contains("Hardware-level"));
        hardware.Confidence.Should().Be(FindingConfidence.High);
        hardware.Explanation.Should().Contain("0x124");
    }

    [Fact]
    public void WheaFinding_SaysCorrectedErrorsAreNotAutomaticallyUrgent()
    {
        // Most WHEA entries are corrected errors. Presenting every one as a
        // failing CPU would be exactly the fear-based framing the doctrine bans.
        CrashCorrelator.Correlate([Event(ReliabilityEventKind.HardwareError)])
            .Single(f => f.Headline.Contains("Hardware-level"))
            .Explanation.Should().Contain("corrected");
    }

    // ── Storage ───────────────────────────────────────────────────────────────

    [Fact]
    public void RepeatedDiskFaults_AreHighConfidence_AndSayToBackUpFirst()
    {
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.DiskFault, hoursAgo: 1),
            Event(ReliabilityEventKind.DiskFault, hoursAgo: 5),
            Event(ReliabilityEventKind.DiskFault, hoursAgo: 9),
        ]);

        var disk = findings.Single(f => f.Headline.Contains("Storage"));
        disk.Confidence.Should().Be(FindingConfidence.High);
        disk.SuggestedChecks.Should().Contain(c => c.Contains("Back up"));
    }

    // ── A shared faulting module ──────────────────────────────────────────────

    [Fact]
    public void AModuleFaultingAcrossSeveralApps_BecomesItsOwnFinding()
    {
        // The signal that is invisible without counting: one component failing in
        // programs that have nothing else in common.
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.ApplicationCrash, hoursAgo: 1, component: "nvlddmkm.dll", processName: "Cod.exe"),
            Event(ReliabilityEventKind.ApplicationCrash, hoursAgo: 3, component: "nvlddmkm.dll", processName: "chrome.exe"),
            Event(ReliabilityEventKind.ApplicationCrash, hoursAgo: 5, component: "nvlddmkm.dll", processName: "Discord.exe"),
        ]);

        var shared = findings.Single(f => f.Headline.Contains("nvlddmkm.dll"));
        shared.Confidence.Should().Be(FindingConfidence.High);
        shared.Headline.Should().Contain("3 different applications");
        shared.Explanation.Should().Contain("shared component");
    }

    [Fact]
    public void OneModuleFaultingInOneAppOnly_IsNotASharedModuleFinding()
    {
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.ApplicationCrash, hoursAgo: 1, component: "cod.dll", processName: "Cod.exe"),
            Event(ReliabilityEventKind.ApplicationCrash, hoursAgo: 3, component: "cod.dll", processName: "Cod.exe"),
        ]);

        findings.Should().NotContain(f => f.Headline.Contains("different applications"));
    }

    // ── Individual applications ───────────────────────────────────────────────

    [Fact]
    public void OneAppCrashOnly_IsNotReportedAsAPattern()
    {
        var findings = CrashCorrelator.Correlate(
            [Event(ReliabilityEventKind.ApplicationCrash, processName: "Discord.exe")]);

        findings.Should().NotContain(f => f.Headline.Contains("Discord.exe"));
    }

    [Fact]
    public void ARepeatedlyFailingApp_IsReported_ButNotAsTheCauseOfSystemCrashes()
    {
        // This is the framing that was wrong before: a crashing application
        // normally takes only itself down. It is a clue, not a verdict.
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.ApplicationCrash, hoursAgo: 1, processName: "Discord.exe"),
            Event(ReliabilityEventKind.ApplicationHang,  hoursAgo: 4, processName: "Discord.exe"),
            Event(ReliabilityEventKind.ApplicationCrash, hoursAgo: 9, processName: "Discord.exe"),
        ]);

        var app = findings.Single(f => f.Headline.Contains("Discord.exe"));
        app.Occurrences.Should().Be(3);
        app.Confidence.Should().Be(FindingConfidence.Moderate);
        app.Explanation.Should().Contain("does not by itself explain");
    }

    // ── Ranking and evidence ──────────────────────────────────────────────────

    [Fact]
    public void FindingsAreOrderedByConfidence()
    {
        var findings = CrashCorrelator.Correlate(
        [
            // Low: a single app pattern that will not even register
            Event(ReliabilityEventKind.ApplicationCrash, hoursAgo: 2, processName: "notepad.exe"),
            // High: three shutdowns
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 1),
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 25),
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 50),
        ]);

        findings.Should().NotBeEmpty();
        findings.Select(f => f.Confidence).Should().BeInDescendingOrder();
        findings[0].Confidence.Should().Be(FindingConfidence.High);
    }

    [Fact]
    public void EveryFindingCarriesItsEvidence_SoNothingIsAsserted()
    {
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 1),
            Event(ReliabilityEventKind.UnexpectedShutdown, hoursAgo: 20),
        ]);

        findings.Should().OnlyContain(f => f.Evidence.Count > 0);
    }

    [Fact]
    public void EvidenceIsCapped_SoOneFindingCannotBuryTheReport()
    {
        var many = Enumerable.Range(1, 40)
            .Select(i => Event(ReliabilityEventKind.DiskFault, hoursAgo: i))
            .ToList();

        var disk = CrashCorrelator.Correlate(many).Single(f => f.Headline.Contains("Storage"));

        disk.Occurrences.Should().Be(40);          // the count is honest
        disk.Evidence.Should().HaveCount(5);       // the attached detail is bounded
    }

    [Fact]
    public void EvidenceIsNewestFirst()
    {
        var findings = CrashCorrelator.Correlate(
        [
            Event(ReliabilityEventKind.DiskFault, hoursAgo: 50),
            Event(ReliabilityEventKind.DiskFault, hoursAgo: 1),
            Event(ReliabilityEventKind.DiskFault, hoursAgo: 20),
        ]);

        var disk = findings.Single(f => f.Headline.Contains("Storage"));
        disk.Evidence.Select(e => e.When).Should().BeInDescendingOrder();
        disk.LastSeen.Should().Be(Now.AddHours(-1));
    }
}
