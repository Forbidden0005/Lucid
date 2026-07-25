using FluentAssertions;
using Lucid.Services.Governance;
using Moq;
using Xunit;

namespace Lucid.Tests.Governance;

/// <summary>
/// Verifies the bridge between mode changes and telemetry polling: registered
/// targets receive the current intervals immediately, mode changes push only
/// values that actually changed, and one broken target never blocks the rest.
/// </summary>
public sealed class PollingCoordinatorTests
{
    private static readonly TimeSpan NormalTel    = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan NormalProc   = TimeSpan.FromSeconds(4.5);
    private static readonly TimeSpan HighLoadTel  = TimeSpan.FromSeconds(3.0);
    private static readonly TimeSpan HighLoadProc = TimeSpan.FromSeconds(9.0);

    [Fact]
    public void RegisterTarget_ImmediatelyPushesCurrentIntervals()
    {
        var coordinator = new PollingCoordinator();
        var target = new Mock<IAdaptiveTelemetryTarget>();

        coordinator.RegisterTarget(target.Object);

        target.Verify(t => t.SetTelemetryInterval(NormalTel),      Times.Once);
        target.Verify(t => t.SetProcessSampleInterval(NormalProc), Times.Once);
    }

    [Fact]
    public void RegisterTarget_Null_Throws()
    {
        var coordinator = new PollingCoordinator();

        var act = () => coordinator.RegisterTarget(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterTarget_Duplicate_IsNotNotifiedTwicePerModeChange()
    {
        var coordinator = new PollingCoordinator();
        var target = new Mock<IAdaptiveTelemetryTarget>();
        coordinator.RegisterTarget(target.Object);
        coordinator.RegisterTarget(target.Object); // duplicate — must not be added twice

        coordinator.ApplyMode(RuntimeMode.HighLoad);

        target.Verify(t => t.SetTelemetryInterval(HighLoadTel),      Times.Once);
        target.Verify(t => t.SetProcessSampleInterval(HighLoadProc), Times.Once);
    }

    [Fact]
    public void ApplyMode_SameMode_DoesNotNotifyTargets()
    {
        var coordinator = new PollingCoordinator();
        var target = new Mock<IAdaptiveTelemetryTarget>();
        coordinator.RegisterTarget(target.Object);
        target.Invocations.Clear(); // discard the registration push

        coordinator.ApplyMode(RuntimeMode.Normal); // Normal → Normal

        target.VerifyNoOtherCalls();
    }

    [Fact]
    public void ApplyMode_NewMode_PushesBothChangedIntervalsAndUpdatesState()
    {
        var coordinator = new PollingCoordinator();
        var target = new Mock<IAdaptiveTelemetryTarget>();
        coordinator.RegisterTarget(target.Object);

        coordinator.ApplyMode(RuntimeMode.HighLoad);

        target.Verify(t => t.SetTelemetryInterval(HighLoadTel),      Times.Once);
        target.Verify(t => t.SetProcessSampleInterval(HighLoadProc), Times.Once);
        coordinator.CurrentMode.Should().Be(RuntimeMode.HighLoad);
        coordinator.CurrentTelemetryInterval.Should().Be(HighLoadTel);
        coordinator.CurrentProcessInterval.Should().Be(HighLoadProc);
    }

    [Fact]
    public void ApplyMode_ThrowingTarget_DoesNotBlockOtherTargets()
    {
        var coordinator = new PollingCoordinator();
        // Throw only for the LowPower interval — RegisterTarget's initial push
        // is NOT exception-guarded in production (only ApplyMode is), so a
        // mock that throws unconditionally would fail registration itself.
        var broken  = new Mock<IAdaptiveTelemetryTarget>();
        broken.Setup(t => t.SetTelemetryInterval(TimeSpan.FromSeconds(6.0)))
              .Throws(new InvalidOperationException("simulated target failure"));
        var healthy = new Mock<IAdaptiveTelemetryTarget>();
        coordinator.RegisterTarget(broken.Object);
        coordinator.RegisterTarget(healthy.Object);

        var act = () => coordinator.ApplyMode(RuntimeMode.LowPower);

        act.Should().NotThrow();
        healthy.Verify(t => t.SetTelemetryInterval(TimeSpan.FromSeconds(6.0)), Times.Once);
    }

    [Fact]
    public void LateRegistration_ReceivesTheModeAppliedBeforeIt()
    {
        var coordinator = new PollingCoordinator();
        coordinator.ApplyMode(RuntimeMode.Gaming);

        var target = new Mock<IAdaptiveTelemetryTarget>();
        coordinator.RegisterTarget(target.Object);

        target.Verify(t => t.SetTelemetryInterval(TimeSpan.FromSeconds(4.0)),       Times.Once);
        target.Verify(t => t.SetProcessSampleInterval(TimeSpan.FromSeconds(15.0)), Times.Once);
    }
}
