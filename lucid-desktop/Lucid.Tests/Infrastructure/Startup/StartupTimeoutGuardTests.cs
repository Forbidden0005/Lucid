using FluentAssertions;
using Lucid.Services.Infrastructure.Startup;
using Xunit;

namespace Lucid.Tests.Infrastructure.Startup;

public sealed class StartupTimeoutGuardTests
{
    [Fact]
    public void Run_SuccessfulTask_ReturnsSucceededSnapshot()
    {
        var snapshot = StartupTimeoutGuard.Run(
            "test-step",
            () => Task.CompletedTask,
            TimeSpan.FromSeconds(5));

        snapshot.Succeeded.Should().BeTrue();
        snapshot.StepName.Should().Be("test-step");
        snapshot.ErrorMessage.Should().BeNull();
        snapshot.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void Run_FailingTask_ReturnsFailedSnapshot()
    {
        var snapshot = StartupTimeoutGuard.Run(
            "failing-step",
            () => Task.FromException(new InvalidOperationException("boom")),
            TimeSpan.FromSeconds(5));

        snapshot.Succeeded.Should().BeFalse();
        snapshot.StepName.Should().Be("failing-step");
        snapshot.ErrorMessage.Should().Contain("boom");
    }

    [Fact]
    public void Run_TimedOutTask_ReturnsFailedSnapshot()
    {
        // Task that never completes — should time out
        var snapshot = StartupTimeoutGuard.Run(
            "timeout-step",
            () => Task.Delay(TimeSpan.FromMinutes(10)),
            TimeSpan.FromMilliseconds(200));  // Very short timeout

        snapshot.Succeeded.Should().BeFalse();
        snapshot.ErrorMessage.Should().Contain("timed out",
            because: "timeout should produce a descriptive error message");
    }

    [Fact]
    public void Run_NeverThrows()
    {
        // Even with a null factory (will throw NullReferenceException), must not propagate
        Func<Task>? factory = null;

        var act = () => StartupTimeoutGuard.Run("null-factory", factory!, TimeSpan.FromSeconds(1));

        act.Should().NotThrow(because: "StartupTimeoutGuard is designed to never throw");
    }

    [Fact]
    public void Run_CapturesElapsedTime()
    {
        var snapshot = StartupTimeoutGuard.Run(
            "delay-step",
            async () => await Task.Delay(50),
            TimeSpan.FromSeconds(5));

        snapshot.Succeeded.Should().BeTrue();
        snapshot.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(40),
            because: "a 50ms task should report at least ~40ms elapsed");
    }
}
