namespace Lucid.Core.Extensions;

/// <summary>
/// DateTime helpers used across the Lucid platform for relative-time
/// formatting and timeline bucketing.
/// </summary>
public static class DateTimeExtensions
{
    /// <summary>
    /// Returns a human-readable relative time string, e.g. "just now",
    /// "3 minutes ago", "yesterday", "5 days ago".
    /// </summary>
    public static string ToRelativeString(this DateTime dt)
    {
        var elapsed = DateTime.Now - dt;

        return elapsed.TotalSeconds switch
        {
            < 60     => "just now",
            < 3600   => $"{(int)elapsed.TotalMinutes} minute{Plural((int)elapsed.TotalMinutes)} ago",
            < 86400  => $"{(int)elapsed.TotalHours} hour{Plural((int)elapsed.TotalHours)} ago",
            < 172800 => "yesterday",
            _        => $"{(int)elapsed.TotalDays} day{Plural((int)elapsed.TotalDays)} ago",
        };
    }

    /// <summary>
    /// Returns true if the DateTime falls within today's calendar date.
    /// </summary>
    public static bool IsToday(this DateTime dt) =>
        dt.Date == DateTime.Today;

    /// <summary>
    /// Returns true if the DateTime is within the last <paramref name="minutes"/> minutes.
    /// </summary>
    public static bool IsWithinMinutes(this DateTime dt, int minutes) =>
        (DateTime.Now - dt).TotalMinutes <= minutes;

    private static string Plural(int count) => count == 1 ? "" : "s";
}
