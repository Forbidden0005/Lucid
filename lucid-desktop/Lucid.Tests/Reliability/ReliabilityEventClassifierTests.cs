using FluentAssertions;
using Lucid.Services.Reliability;
using Xunit;

namespace Lucid.Tests.Reliability;

/// <summary>
/// Classification of raw Windows event records.
///
/// These tests are the reason the reliability domain is split the way it is:
/// every case here would otherwise require a machine that had genuinely crashed
/// in a specific way.
/// </summary>
public sealed class ReliabilityEventClassifierTests
{
    private static readonly DateTimeOffset At =
        new(new DateTime(2026, 8, 14, 22, 15, 0, DateTimeKind.Local));

    private static RawEventRecord Record(
        string          provider,
        int             id,
        byte            level      = 2,
        string?         message    = null,
        string[]?       properties = null,
        string          logName    = "System")
        => new()
        {
            LogName      = logName,
            ProviderName = provider,
            EventId      = id,
            Level        = level,
            TimeCreated  = At,
            Message      = message,
            Properties   = properties ?? [],
        };

    // ── Machine-level failures ────────────────────────────────────────────────

    [Fact]
    public void KernelPower41_IsAnUnexpectedShutdown()
    {
        var result = ReliabilityEventClassifier.Classify(
            Record("Microsoft-Windows-Kernel-Power", 41, level: 1));

        result.Should().NotBeNull();
        result!.Kind.Should().Be(ReliabilityEventKind.UnexpectedShutdown);
        result.IsSevere.Should().BeTrue();
    }

    [Fact]
    public void EventLog6008_IsAnUnexpectedShutdown()
        => ReliabilityEventClassifier.Classify(Record("EventLog", 6008))!
               .Kind.Should().Be(ReliabilityEventKind.UnexpectedShutdown);

    [Fact]
    public void BugCheck1001_IsAStopError_AndCarriesItsCode()
    {
        var result = ReliabilityEventClassifier.Classify(Record(
            "BugCheck", 1001,
            message: "The computer has rebooted from a bugcheck. The bugcheck was: " +
                     "0x0000009f (0x0000000000000003, 0xffff8e0f1b4c0060)."));

        result!.Kind.Should().Be(ReliabilityEventKind.BugCheck);
        result.StopCode.Should().Be("0x0000009F");   // normalised to upper case
        result.Summary.Should().Contain("0x0000009F");
    }

    [Fact]
    public void KernelPower41_WithAZeroBugcheckCode_ReportsNoStopCode()
    {
        // Kernel-Power 41 always carries a bugcheck property; it is all zeros for
        // an ordinary power loss. Reporting that as a stop code would invent a
        // blue screen that never happened.
        var result = ReliabilityEventClassifier.Classify(Record(
            "Microsoft-Windows-Kernel-Power", 41, level: 1,
            properties: ["0x0000000000000000", "0"]));

        result!.StopCode.Should().BeNull();
        result.Summary.Should().NotContain("stop error (");
        result.Summary.Should().Contain("power loss");
    }

    [Fact]
    public void KernelPower41_WithARealBugcheckCode_ReportsIt()
    {
        var result = ReliabilityEventClassifier.Classify(Record(
            "Microsoft-Windows-Kernel-Power", 41, level: 1,
            properties: ["0x00000133", "0"]));

        result!.StopCode.Should().Be("0x00000133");
        result.Summary.Should().Contain("0x00000133");
    }

    [Fact]
    public void WheaLogger_IsAHardwareError_AtAnyLevel()
    {
        // WHEA event 47 is Information level — a corrected error. It still belongs
        // in the report, because a run of corrected errors is itself a signal.
        var result = ReliabilityEventClassifier.Classify(
            Record("Microsoft-Windows-WHEA-Logger", 47, level: 4));

        result!.Kind.Should().Be(ReliabilityEventKind.HardwareError);
    }

    [Theory]
    [InlineData("disk", 51)]
    [InlineData("Ntfs", 55)]
    [InlineData("volmgr", 46)]
    [InlineData("stornvme", 129)]
    public void StorageProviders_AreDiskFaults(string provider, int id)
        => ReliabilityEventClassifier.Classify(Record(provider, id))!
               .Kind.Should().Be(ReliabilityEventKind.DiskFault);

    // ── Services and drivers ─────────────────────────────────────────────────

    [Theory]
    [InlineData(7031)]
    [InlineData(7034)]
    public void ServiceControlManager_TerminationIds_AreServiceFailures(int id)
        => ReliabilityEventClassifier.Classify(Record("Service Control Manager", id))!
               .Kind.Should().Be(ReliabilityEventKind.ServiceFailure);

    [Theory]
    [InlineData(7000)]
    [InlineData(7026)]
    public void ServiceControlManager_LoadFailureIds_AreDriverFaults(int id)
        => ReliabilityEventClassifier.Classify(Record("Service Control Manager", id))!
               .Kind.Should().Be(ReliabilityEventKind.DriverFault);

    [Fact]
    public void ServiceControlManager_NamesTheService()
        => ReliabilityEventClassifier.Classify(Record(
               "Service Control Manager", 7031, properties: ["Windows Audio"]))!
               .Component.Should().Be("Windows Audio");

    // ── Applications ─────────────────────────────────────────────────────────

    [Fact]
    public void ApplicationError1000_NamesTheAppAndTheFaultingModule()
    {
        // Property order is fixed by the publisher: app, version, timestamp, module.
        var result = ReliabilityEventClassifier.Classify(Record(
            "Application Error", 1000, logName: "Application",
            properties: ["Cod.exe", "1.0.0.0", "abc123", "nvlddmkm.dll", "31.0.15", "def456", "c0000005"]));

        result!.Kind.Should().Be(ReliabilityEventKind.ApplicationCrash);
        result.ProcessName.Should().Be("Cod.exe");
        result.Component.Should().Be("nvlddmkm.dll");
        result.Summary.Should().Contain("Cod.exe").And.Contain("nvlddmkm.dll");
    }

    [Fact]
    public void ApplicationHang1002_IsAHang()
    {
        var result = ReliabilityEventClassifier.Classify(Record(
            "Application Hang", 1002, logName: "Application",
            properties: ["Discord.exe"]));

        result!.Kind.Should().Be(ReliabilityEventKind.ApplicationHang);
        result.ProcessName.Should().Be("Discord.exe");
        result.Summary.Should().Contain("stopped responding");
    }

    [Fact]
    public void WindowsErrorReporting_LiveKernelEvent_IsTreatedAsAStopError()
        => ReliabilityEventClassifier.Classify(Record(
               "Windows Error Reporting", 1001, logName: "Application",
               message: "Fault bucket LiveKernelEvent 141"))!
               .Kind.Should().Be(ReliabilityEventKind.BugCheck);

    // ── Robustness ───────────────────────────────────────────────────────────

    [Fact]
    public void MissingMessage_IsToleratedRatherThanDroppingTheEvent()
    {
        // FormatDescription fails whenever a publisher's message resources are
        // absent — routine for third-party drivers. The event must survive it.
        var result = ReliabilityEventClassifier.Classify(
            Record("Microsoft-Windows-Kernel-Power", 41, level: 1, message: null));

        result.Should().NotBeNull();
        result!.Summary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void MissingProperties_AreToleratedRatherThanThrowing()
    {
        // An Application Error with no properties at all should still classify;
        // it just cannot name the app.
        var result = ReliabilityEventClassifier.Classify(Record(
            "Application Error", 1000, logName: "Application", properties: []));

        result!.Kind.Should().Be(ReliabilityEventKind.ApplicationCrash);
        result.ProcessName.Should().BeNull();
        result.Summary.Should().Be("An application closed unexpectedly.");
    }

    [Fact]
    public void UnknownEventId_FromAQueriedPublisher_SurvivesIfWindowsCalledItSevere()
    {
        // Dropping a Critical event because this codebase has not heard of the ID
        // is how a real cause gets missed.
        var result = ReliabilityEventClassifier.Classify(Record(
            "Microsoft-Windows-Kernel-Boot", 99999, level: 1,
            message: "Something unusual happened.\r\nMore boilerplate follows."));

        result.Should().NotBeNull();
        result!.Kind.Should().Be(ReliabilityEventKind.Other);
        result.Summary.Should().Be("Something unusual happened.");   // first line only
    }

    [Fact]
    public void UnknownEventId_AtInformationLevel_IsDropped()
        => ReliabilityEventClassifier.Classify(
               Record("Some-Chatty-Provider", 4242, level: 4)).Should().BeNull();

    [Fact]
    public void ProviderMatching_IsCaseInsensitive()
        => ReliabilityEventClassifier.Classify(Record("microsoft-windows-kernel-power", 41))!
               .Kind.Should().Be(ReliabilityEventKind.UnexpectedShutdown);

    [Fact]
    public void ClassifyAll_DropsIrrelevantRecordsAndKeepsTheRest()
    {
        var records = new[]
        {
            Record("Microsoft-Windows-Kernel-Power", 41, level: 1),
            Record("Some-Chatty-Provider", 4242, level: 4),      // dropped
            Record("BugCheck", 1001, message: "bugcheck was: 0x00000133"),
        };

        ReliabilityEventClassifier.ClassifyAll(records).Should().HaveCount(2);
    }

    // ── Query specs ──────────────────────────────────────────────────────────

    [Fact]
    public void ServiceControlManagerQuery_ExcludesTheStateChangeEvent()
    {
        // 7036 fires every time any service starts or stops. Including it would
        // mean reading thousands of irrelevant records to find a handful.
        var scm = ReliabilityEventClassifier.SystemQuery
            .Single(s => s.ProviderName == "Service Control Manager");

        scm.EventIds.Should().NotBeEmpty();
        scm.EventIds.Should().NotContain(7036);
    }

    [Fact]
    public void LowVolumePublishers_AreQueriedWithoutAnIdFilter()
    {
        // So that an unrecognised-but-severe event still reaches the classifier.
        ReliabilityEventClassifier.SystemQuery
            .Single(s => s.ProviderName == "Microsoft-Windows-WHEA-Logger")
            .EventIds.Should().BeEmpty();
    }
}
