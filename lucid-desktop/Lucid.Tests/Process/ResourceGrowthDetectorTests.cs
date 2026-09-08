using FluentAssertions;
using Lucid.Services.ProcessIntel;
using Xunit;

namespace Lucid.Tests.Process;

/// <summary>
/// Growth detection for handle and thread counts.
///
/// These tests exist because of a specific wrong answer: with absolute
/// thresholds (handles &gt; 2000, threads &gt; 200), Lucid reported Discord and
/// Call of Duty as leaking handles simply for being large applications, and then
/// offered that as an explanation for the machine crashing. The cases below pin
/// down the distinction it was missing — a big number is not a leak, a rising
/// one is.
/// </summary>
public sealed class ResourceGrowthDetectorTests
{
    // Same values the tracker uses for handles.
    private const int    MinSamples = 12;
    private const double Relative   = 0.20;
    private const int    Absolute   = 400;

    private static bool Detect(IEnumerable<int> samples) =>
        ResourceGrowthDetector.IsSustainedGrowth(
            samples.ToList(), MinSamples, Relative, Absolute);

    /// <summary>A flat series at <paramref name="value"/>, as a big-but-healthy app looks.</summary>
    private static List<int> Flat(int value, int count = 20) =>
        Enumerable.Repeat(value, count).ToList();

    /// <summary>A steadily climbing series.</summary>
    private static List<int> Climbing(int start, int step, int count = 20) =>
        Enumerable.Range(0, count).Select(i => start + i * step).ToList();

    // ── The false positives this replaced ─────────────────────────────────────

    [Fact]
    public void AProcessHoldingManyHandlesSteadily_IsNotGrowth()
    {
        // Discord idling at 3,200 handles. The old absolute threshold called this
        // a handle leak; it is simply a large application doing nothing wrong.
        Detect(Flat(3_200)).Should().BeFalse();
    }

    [Fact]
    public void AProcessHoldingAVeryLargeNumberOfHandlesSteadily_IsStillNotGrowth()
        => Detect(Flat(30_000)).Should().BeFalse();

    [Fact]
    public void NormalJitterAroundALargeBaseline_IsNotGrowth()
    {
        // Real counts wobble. Wobble is not a trend.
        var jittery = new List<int>
        {
            3200, 3215, 3190, 3230, 3205, 3180, 3220, 3195,
            3210, 3225, 3185, 3200, 3218, 3192, 3208, 3199,
            3212, 3188, 3222, 3201,
        };

        Detect(jittery).Should().BeFalse();
    }

    // ── The real thing ───────────────────────────────────────────────────────

    [Fact]
    public void ASteadyClimb_IsGrowth()
    {
        // 2,000 → 3,900 across the window: +95%, +1,900 handles, monotonic.
        Detect(Climbing(2_000, 100)).Should().BeTrue();
    }

    [Fact]
    public void AClimbFromASmallBaseline_IsGrowthOnceItIsBigEnoughToMatter()
        => Detect(Climbing(500, 50)).Should().BeTrue();   // 500 → 1,450

    // ── Each condition is load-bearing ───────────────────────────────────────

    [Fact]
    public void GrowthTooSmallInAbsoluteTerms_IsIgnored()
    {
        // 100 → 199 doubles in relative terms, but 99 extra handles is nothing.
        // Without the absolute floor, every small process would be flagged.
        Detect(Climbing(100, 5)).Should().BeFalse();
    }

    [Fact]
    public void GrowthTooSmallInRelativeTerms_IsIgnored()
    {
        // 30,000 → 30,570: over the 400 absolute floor, but under 2% of the
        // baseline. On a process this size that is ordinary variation.
        Detect(Climbing(30_000, 30)).Should().BeFalse();
    }

    [Fact]
    public void TooLittleHistory_IsNoJudgement()
    {
        // Never call a leak from three samples, however dramatic they look.
        Detect(Climbing(1_000, 500, count: 3)).Should().BeFalse();
    }

    [Fact]
    public void ASingleSpikeAtTheEnd_IsNotSustainedGrowth()
    {
        // Flat for the whole window, then one jump. Something momentary happened;
        // that is not a climb, and reporting it as one would be a false alarm on
        // any process that briefly opens a lot of files.
        var spike = Flat(1_000, 19);
        spike.Add(5_000);

        Detect(spike).Should().BeFalse();
    }

    [Fact]
    public void GrowthThatAlreadyStartedByTheMidpoint_Counts()
    {
        // Rises through the first half, then plateaus. Still a real climb.
        var rampThenFlat = Climbing(1_000, 150, count: 10)
            .Concat(Flat(2_350, 10))
            .ToList();

        Detect(rampThenFlat).Should().BeTrue();
    }

    [Fact]
    public void ADecliningSeries_IsNotGrowth()
        => Detect(Climbing(5_000, -100)).Should().BeFalse();

    [Fact]
    public void ASeriesThatRoseAndCameBackDown_IsNotGrowth()
    {
        // The defining property of a leak is that it does not come back.
        var upThenDown = Climbing(1_000, 200, count: 10)
            .Concat(Climbing(2_800, -200, count: 10))
            .ToList();

        Detect(upThenDown).Should().BeFalse();
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void AZeroBaseline_FallsBackToTheAbsoluteRequirement()
    {
        // A ratio against zero is meaningless, so the absolute floor decides.
        var fromZero = new List<int> { 0 }
            .Concat(Climbing(0, 60, count: 19))
            .ToList();

        Detect(fromZero).Should().BeTrue();
    }

    [Fact]
    public void AZeroBaselineWithTinyGrowth_IsStillIgnored()
    {
        var fromZero = new List<int> { 0 }
            .Concat(Climbing(0, 2, count: 19))
            .ToList();

        Detect(fromZero).Should().BeFalse();
    }

    [Fact]
    public void NoSamples_IsNoJudgement()
        => Detect([]).Should().BeFalse();

    [Fact]
    public void ThreadThresholds_BehaveTheSameWay()
    {
        // The thread settings are looser (+30%, +24) because thread counts are
        // smaller. A game sitting flat at 400 threads must still not be flagged.
        var flatHighThreads = Enumerable.Repeat(400, 20).ToList();
        ResourceGrowthDetector.IsSustainedGrowth(flatHighThreads, 12, 0.30, 24)
            .Should().BeFalse();

        var climbingThreads = Enumerable.Range(0, 20).Select(i => 60 + i * 5).ToList();
        ResourceGrowthDetector.IsSustainedGrowth(climbingThreads, 12, 0.30, 24)
            .Should().BeTrue();
    }
}
