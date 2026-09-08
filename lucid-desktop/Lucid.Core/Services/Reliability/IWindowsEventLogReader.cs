namespace Lucid.Services.Reliability;

/// <summary>
/// One publisher to query, and optionally the specific event IDs worth reading
/// from it.
///
/// The ID list matters for volume, not just tidiness. "Service Control Manager"
/// logs an Information event every time any service starts or stops — thousands
/// a day — so asking for that publisher without narrowing to the failure IDs
/// would read a great deal of data to find nothing. Publishers that only ever
/// log something worth knowing about (WHEA, BugCheck) are queried wholesale, so
/// a reliability event with an ID this codebase has not seen before still
/// surfaces.
/// </summary>
public sealed record EventQuerySpec
{
    public required string ProviderName { get; init; }

    /// <summary>
    /// Event IDs to include. Empty means every event from this publisher.
    /// </summary>
    public IReadOnlyList<int> EventIds { get; init; } = [];
}

/// <summary>
/// Reads raw records from the Windows event logs.
///
/// This is the only part of the reliability domain that touches Windows. It
/// exists as an interface so that classification and correlation — where all the
/// judgement lives — can be tested against constructed records instead of
/// requiring a machine that has actually crashed.
///
/// Implementations must:
///   • Return records newest-first.
///   • Respect <c>maxRecords</c> strictly. The System log on a long-lived
///     machine holds hundreds of thousands of entries; an unbounded read is
///     exactly the kind of operation that would make Lucid the reason the PC is
///     slow.
///   • Throw rather than return an empty list when the log cannot be read, so
///     "could not look" is never reported to the user as "nothing found".
/// </summary>
public interface IWindowsEventLogReader
{
    /// <summary>
    /// Reads reliability-relevant records from one log, newest first.
    /// </summary>
    /// <param name="logName">"System" or "Application".</param>
    /// <param name="specs">Publishers, and the IDs of interest within them.</param>
    /// <param name="since">Oldest event to consider.</param>
    /// <param name="maxRecords">Hard cap on returned records.</param>
    Task<IReadOnlyList<RawEventRecord>> ReadAsync(
        string                       logName,
        IReadOnlyList<EventQuerySpec> specs,
        DateTimeOffset               since,
        int                          maxRecords,
        CancellationToken            ct = default);
}
