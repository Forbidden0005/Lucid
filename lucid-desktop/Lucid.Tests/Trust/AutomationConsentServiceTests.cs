using FluentAssertions;
using Lucid.Services.Automation;
using Lucid.Services.Timeline;
using Lucid.Services.Trust;
using Xunit;

namespace Lucid.Tests.Trust;

/// <summary>
/// Covers the automation consent gate behavior without requiring the live UI
/// dispatcher or companion overlay.
/// </summary>
public sealed class AutomationConsentServiceTests : IDisposable
{
    private readonly string _ledgerDir = Path.Combine(
        Path.GetTempPath(), "lucid-consent-tests", Guid.NewGuid().ToString("N"));

    private readonly List<AutomationAuditService> _auditServices = [];

    public void Dispose()
    {
        foreach (var audit in _auditServices)
            audit.Dispose();

        try
        {
            if (Directory.Exists(_ledgerDir))
                Directory.Delete(_ledgerDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task RequestConsentAsync_BlockedBoundaryAction_ReturnsFalseWithoutPrompt()
    {
        var timeline = new TestTimelineService();
        var service = CreateService(timeline);
        var prompted = false;
        service.ConsentRequired += (_, _) => prompted = true;
        var step = NewStep("automation.credentials.read", AutomationRiskLevel.High);

        var approved = await service.RequestConsentAsync(step, "wf-blocked");

        approved.Should().BeFalse();
        prompted.Should().BeFalse();
        timeline.Events.Should().ContainSingle(ev =>
            ev.Type == TimelineEventType.AutomationBoundaryBlocked &&
            ev.Title.Contains("Blocked"));
    }

    [Fact]
    public async Task RequestConsentAsync_LowRiskDefaultMode_AutoApprovesWithoutPrompt()
    {
        var service = CreateService(new TestTimelineService());
        var prompted = false;
        service.ConsentRequired += (_, _) => prompted = true;
        var step = NewStep("automation.launch.task-manager", AutomationRiskLevel.Low);

        var approved = await service.RequestConsentAsync(step, "wf-auto");

        approved.Should().BeTrue();
        prompted.Should().BeFalse();
    }

    [Fact]
    public async Task RequestConsentAsync_ObserveOnly_DeniesHighRiskWithoutPrompt()
    {
        var service = CreateService(new TestTimelineService());
        service.SetMode(TrustConsentMode.ObserveOnly);
        var prompted = false;
        service.ConsentRequired += (_, _) => prompted = true;
        var step = NewStep("action.repair.sfc-scannow", AutomationRiskLevel.High);

        var approved = await service.RequestConsentAsync(step, "wf-observe");

        approved.Should().BeFalse();
        prompted.Should().BeFalse();
    }

    [Fact]
    public async Task RequestConsentAsync_AskAlways_RaisesPromptAndReturnsApproval()
    {
        var timeline = new TestTimelineService();
        var service = CreateService(timeline);
        service.SetMode(TrustConsentMode.AskAlways);
        ConsentRequest? seenRequest = null;
        service.ConsentRequired += (_, args) =>
        {
            seenRequest = args.Request;
            args.Approve?.Invoke();
        };
        var granted = false;
        service.ConsentGranted += (_, _) => granted = true;
        var step = NewStep("action.network.flush-dns", AutomationRiskLevel.Low);

        var approved = await service.RequestConsentAsync(step, "wf-approve", activeInsight: "DNS cache issue");

        approved.Should().BeTrue();
        granted.Should().BeTrue();
        seenRequest.Should().NotBeNull();
        seenRequest!.Scope.Should().Be(PermissionScope.NetworkReset);
        timeline.Events.Should().Contain(ev => ev.Type == TimelineEventType.ConsentRequested);
        timeline.Events.Should().Contain(ev => ev.Type == TimelineEventType.ConsentGranted);
    }

    [Fact]
    public async Task RequestConsentAsync_AskAlways_RaisesPromptAndReturnsDenial()
    {
        var timeline = new TestTimelineService();
        var service = CreateService(timeline);
        service.SetMode(TrustConsentMode.AskAlways);
        var denied = false;
        service.ConsentRequired += (_, args) => args.Deny?.Invoke();
        service.ConsentDenied += (_, _) => denied = true;
        var step = NewStep("action.network.flush-dns", AutomationRiskLevel.Low);

        var approved = await service.RequestConsentAsync(step, "wf-deny", activeInsight: "DNS cache issue");

        approved.Should().BeFalse();
        denied.Should().BeTrue();
        timeline.Events.Should().Contain(ev => ev.Type == TimelineEventType.ConsentRequested);
        timeline.Events.Should().Contain(ev => ev.Type == TimelineEventType.ConsentDenied);
    }

    [Fact]
    public async Task RequestConsentAsync_WhenCancelledBeforeResponse_TreatsAsDenial()
    {
        var timeline = new TestTimelineService();
        var service = CreateService(timeline);
        service.SetMode(TrustConsentMode.AskAlways);
        var denied = false;
        using var cts = new CancellationTokenSource();
        service.ConsentRequired += (_, _) => cts.Cancel();
        service.ConsentDenied += (_, _) => denied = true;
        var step = NewStep("action.network.flush-dns", AutomationRiskLevel.Low);

        var approved = await service.RequestConsentAsync(
            step,
            "wf-cancel",
            activeInsight: "DNS cache issue",
            ct: cts.Token);

        approved.Should().BeFalse();
        denied.Should().BeTrue();
        timeline.Events.Should().Contain(ev => ev.Type == TimelineEventType.ConsentRequested);
        timeline.Events.Should().Contain(ev => ev.Type == TimelineEventType.ConsentDenied);
    }

    [Fact]
    public void RequiresConsent_MapsModesAsExpected()
    {
        var service = CreateService(new TestTimelineService());

        service.SetMode(TrustConsentMode.AskHighRiskOnly);
        service.RequiresConsent(TrustRiskLevel.Medium).Should().BeFalse();
        service.RequiresConsent(TrustRiskLevel.High).Should().BeTrue();

        service.SetMode(TrustConsentMode.AskForMediumAndHighRisk);
        service.RequiresConsent(TrustRiskLevel.Low).Should().BeFalse();
        service.RequiresConsent(TrustRiskLevel.Medium).Should().BeTrue();
    }

    private AutomationConsentService CreateService(TestTimelineService timeline)
    {
        // Isolated ledger path — tests must never touch the real user ledger.
        // Tracked so Dispose() releases the service's sync primitives and the
        // fixture teardown removes the temp files.
        var audit = new AutomationAuditService(auditFilePath: Path.Combine(
            _ledgerDir, Guid.NewGuid().ToString("N") + ".json"));
        _auditServices.Add(audit);

        return new(
            timeline,
            audit,
            new ConsentExplanationService(),
            new AutomationTransparencyEngine(),
            new ImmediateDispatcher());
    }

    private static AutomationStep NewStep(string actionId, AutomationRiskLevel risk) => new()
    {
        Id = AutomationStep.NewId(),
        Title = "Test Step",
        Narration = "Lucid wants to run a test step.",
        Rationale = "The current condition suggests this step may help.",
        ActionId = actionId,
        Risk = risk,
        RequiresConfirmation = risk >= AutomationRiskLevel.Medium,
        RollbackAvailable = false,
        ExpectedOutcome = "A test outcome will be observed.",
        HowToUndo = "Undo is not required for this test."
    };

    private sealed class ImmediateDispatcher : AutomationConsentService.IUiDispatcher
    {
        public bool HasThreadAccess => true;

        public void TryEnqueue(Action action) => action();
    }

    private sealed class TestTimelineService : ITimelineAggregationService
    {
        private readonly List<TimelineEvent> _events = [];

        public IReadOnlyList<TimelineEvent> Events => _events;

        public event EventHandler<TimelineEvent>? NewEventAdded;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void AddExternalEvent(TimelineEvent ev)
        {
            _events.Add(ev);
            NewEventAdded?.Invoke(this, ev);
        }
    }
}
