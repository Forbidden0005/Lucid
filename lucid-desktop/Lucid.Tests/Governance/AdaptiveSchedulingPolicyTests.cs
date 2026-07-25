using FluentAssertions;
using Lucid.Services.Governance;
using Xunit;

namespace Lucid.Tests.Governance;

/// <summary>
/// Pins the static mode → scheduling-parameter policy table. These values are
/// the documented contract between the governance layer and every consumer
/// (PollingCoordinator, ConcurrencyBudget ceiling, idle-only gate); a silent
/// change here re-tunes the whole platform's background behavior.
/// </summary>
public sealed class AdaptiveSchedulingPolicyTests
{
    [Theory]
    [InlineData(RuntimeMode.Normal,            1.5)]
    [InlineData(RuntimeMode.HighLoad,          3.0)]
    [InlineData(RuntimeMode.LowPower,          6.0)]
    [InlineData(RuntimeMode.Gaming,            4.0)]
    [InlineData(RuntimeMode.ThermalProtection, 2.5)]
    public void GetTelemetryInterval_MatchesDocumentedTable(RuntimeMode mode, double expectedSeconds)
    {
        AdaptiveSchedulingPolicy.GetTelemetryInterval(mode)
            .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(RuntimeMode.Normal,             4.5)]
    [InlineData(RuntimeMode.HighLoad,           9.0)]
    [InlineData(RuntimeMode.LowPower,          20.0)]
    [InlineData(RuntimeMode.Gaming,            15.0)]
    [InlineData(RuntimeMode.ThermalProtection, 12.0)]
    public void GetProcessSampleInterval_MatchesDocumentedTable(RuntimeMode mode, double expectedSeconds)
    {
        AdaptiveSchedulingPolicy.GetProcessSampleInterval(mode)
            .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(RuntimeMode.Normal,            3)]
    [InlineData(RuntimeMode.HighLoad,          1)]
    [InlineData(RuntimeMode.LowPower,          0)]
    [InlineData(RuntimeMode.Gaming,            0)]
    [InlineData(RuntimeMode.ThermalProtection, 0)]
    public void GetMaxConcurrentBackground_MatchesDocumentedCeilings(RuntimeMode mode, int expected)
    {
        AdaptiveSchedulingPolicy.GetMaxConcurrentBackground(mode).Should().Be(expected);
    }

    [Theory]
    [InlineData(RuntimeMode.Normal,            true)]
    [InlineData(RuntimeMode.HighLoad,          false)]
    [InlineData(RuntimeMode.LowPower,          false)]
    [InlineData(RuntimeMode.Gaming,            false)]
    [InlineData(RuntimeMode.ThermalProtection, false)]
    public void AllowIdleOnlyWork_TrueOnlyInNormalMode(RuntimeMode mode, bool expected)
    {
        AdaptiveSchedulingPolicy.AllowIdleOnlyWork(mode).Should().Be(expected);
    }

    [Fact]
    public void GetDeferralDescription_ReturnsNonEmptyForEveryMode()
    {
        foreach (var mode in Enum.GetValues<RuntimeMode>())
        {
            AdaptiveSchedulingPolicy.GetDeferralDescription(mode)
                .Should().NotBeNullOrWhiteSpace();
        }
    }

    /// <summary>
    /// Pins a contract gap found in the 2026-07-25 governance audit:
    /// <see cref="IAdaptiveTelemetryTarget"/> documents "a value of zero means
    /// pause", but no implementation honors it (WindowsTelemetryService clamps
    /// to ≥ 500 ms). The gap stays latent only because the policy table never
    /// emits a zero interval — this test keeps that invariant from breaking
    /// silently. If a future mode legitimately needs pause semantics, implement
    /// the pause contract in every IAdaptiveTelemetryTarget first.
    /// </summary>
    [Fact]
    public void PolicyTable_NeverEmitsZeroIntervals_PausContractIsUnimplemented()
    {
        foreach (var mode in Enum.GetValues<RuntimeMode>())
        {
            AdaptiveSchedulingPolicy.GetTelemetryInterval(mode)
                .Should().BePositive($"mode {mode} must not emit the unimplemented 0=pause sentinel");
            AdaptiveSchedulingPolicy.GetProcessSampleInterval(mode)
                .Should().BePositive($"mode {mode} must not emit the unimplemented 0=pause sentinel");
        }
    }
}
