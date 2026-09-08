namespace Lucid.Services.ProcessIntel;

/// <summary>
/// Decides whether a series of resource counts is genuinely climbing.
///
/// This exists because the obvious approach — flag anything above a fixed number
/// — is wrong in a way that is actively misleading. A browser or a game
/// legitimately holds thousands of handles and hundreds of threads all day
/// without leaking anything, so an absolute threshold reports every large
/// application as broken and says nothing about the ones that really are.
///
/// A leak is a *shape*, not a size: the count keeps rising and does not come
/// back down. That needs three things to be true at once, because each one alone
/// produces false positives:
///
///   • Enough history to have seen a trend rather than a moment.
///   • Meaningful relative growth — 200 extra handles means something very
///     different on a process holding 300 than on one holding 30,000.
///   • Meaningful absolute growth — 30% of a tiny baseline is noise.
///
/// And a midpoint check, so a single spike at the end of the window does not
/// read as a steady climb.
///
/// Pure and allocation-free: this runs for every tracked process on every
/// telemetry tick, so it enumerates once and allocates nothing.
/// </summary>
public static class ResourceGrowthDetector
{
    /// <summary>
    /// True when <paramref name="samples"/> (oldest first) shows sustained growth.
    /// </summary>
    /// <param name="samples">Chronological samples, oldest first.</param>
    /// <param name="minSamples">Minimum history before any judgement is made.</param>
    /// <param name="minRelativeGrowth">
    /// Required growth as a fraction of the oldest sample — 0.20 for 20%.
    /// Ignored when the oldest sample is zero or negative, where a ratio is
    /// meaningless and the absolute requirement carries the decision alone.
    /// </param>
    /// <param name="minAbsoluteGrowth">Required growth in absolute units.</param>
    public static bool IsSustainedGrowth(
        IReadOnlyCollection<int> samples,
        int                      minSamples,
        double                   minRelativeGrowth,
        int                      minAbsoluteGrowth)
    {
        if (samples.Count < minSamples || minSamples <= 0) return false;

        var midIndex = samples.Count / 2;

        int index = 0, oldest = 0, middle = 0, newest = 0;

        foreach (var value in samples)
        {
            if (index == 0)        oldest = value;
            if (index == midIndex) middle = value;
            newest = value;
            index++;
        }

        var absoluteGrowth = newest - oldest;
        if (absoluteGrowth < minAbsoluteGrowth) return false;

        // Sustained, not spiky: by halfway through the window the count should
        // already have moved. A process that sat flat and then jumped once is
        // doing something momentary, not leaking.
        if (middle <= oldest) return false;

        // A ratio against zero or a negative baseline is meaningless, so the
        // absolute requirement above stands alone in that case.
        if (oldest <= 0) return true;

        return (double)absoluteGrowth / oldest >= minRelativeGrowth;
    }
}
