using System.Diagnostics.Eventing.Reader;
using System.Globalization;

namespace Lucid.Services.Reliability;

/// <summary>
/// Reads the Windows event logs through <see cref="EventLogReader"/>.
///
/// The only Windows-touching part of the reliability domain, and deliberately
/// thin: it builds a query, reads records, and hands back
/// <see cref="RawEventRecord"/>s without interpreting any of them.
///
/// Cost control is the main design concern. The System log on a machine that has
/// been running for years holds hundreds of thousands of entries, so:
///   • Filtering happens in the XPath query, letting Windows use its own
///     indexes rather than us reading everything and discarding most of it.
///   • The read stops at <c>maxRecords</c>.
///   • <see cref="EventLogQuery.ReverseDirection"/> reads newest-first, so the
///     cap keeps the most recent events rather than the oldest.
///
/// Reads run on a thread-pool thread because EventLogReader is synchronous and
/// this is called from the UI thread in response to a question.
///
/// Permissions: the System and Application logs are readable by a standard user,
/// so no elevation is needed. The Security log would need it — and is not read.
/// </summary>
public sealed class WindowsEventLogReader : IWindowsEventLogReader
{
    public Task<IReadOnlyList<RawEventRecord>> ReadAsync(
        string                        logName,
        IReadOnlyList<EventQuerySpec> specs,
        DateTimeOffset                since,
        int                           maxRecords,
        CancellationToken             ct = default)
    {
        if (specs.Count == 0 || maxRecords <= 0)
            return Task.FromResult<IReadOnlyList<RawEventRecord>>([]);

        var xpath = BuildXPath(specs, since);

        // No usable spec produced a clause. Returning an unfiltered query here
        // would quietly turn a malformed spec list into "read the entire log",
        // which is the one thing this class exists to avoid.
        if (xpath is null)
            return Task.FromResult<IReadOnlyList<RawEventRecord>>([]);

        // EventLogReader blocks. Hand it to the pool so a question in the chat
        // never stalls the UI thread while Windows walks the log.
        return Task.Run<IReadOnlyList<RawEventRecord>>(
            () => Read(logName, xpath, maxRecords, ct), ct);
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    private static List<RawEventRecord> Read(
        string            logName,
        string            xpath,
        int               maxRecords,
        CancellationToken ct)
    {
        var results = new List<RawEventRecord>(Math.Min(maxRecords, 256));

        var query = new EventLogQuery(logName, PathType.LogName, xpath)
        {
            // Newest first, so the record cap trims history rather than recent events.
            ReverseDirection = true,
        };

        // Construction is where access and existence problems surface. They are
        // deliberately not caught here — the contract is that a failed read
        // throws, so the caller can tell the user "could not look" rather than
        // "nothing found".
        using var reader = new EventLogReader(query);

        while (results.Count < maxRecords)
        {
            ct.ThrowIfCancellationRequested();

            using var record = reader.ReadEvent();
            if (record is null) break;               // end of log

            results.Add(Convert(record));
        }

        return results;
    }

    private static RawEventRecord Convert(EventRecord record) => new()
    {
        LogName      = record.LogName      ?? string.Empty,
        ProviderName = record.ProviderName ?? string.Empty,
        EventId      = record.Id,
        Level        = record.Level        ?? 4,     // absent level reads as Information
        TimeCreated  = ToOffset(record.TimeCreated),
        Message      = SafeDescription(record),
        Properties   = SafeProperties(record),
    };

    // ── XPath construction ────────────────────────────────────────────────────

    /// <summary>
    /// Builds a single query covering every spec, so one pass over the log
    /// answers everything instead of one pass per publisher.
    ///
    /// Shape:
    ///   *[System[((Provider[@Name='A'] and (EventID=41)) or (Provider[@Name='B']))
    ///            and TimeCreated[@SystemTime&gt;='...']]]
    /// </summary>
    /// <returns>The query, or null when no spec produced a usable clause.</returns>
    internal static string? BuildXPath(IReadOnlyList<EventQuerySpec> specs, DateTimeOffset since)
    {
        var clauses = new List<string>(specs.Count);

        foreach (var spec in specs)
        {
            // Provider names come from a static table in this assembly, never from
            // user input. Validated anyway: a stray apostrophe would silently
            // corrupt the query into something that matches the wrong events.
            if (string.IsNullOrWhiteSpace(spec.ProviderName) ||
                spec.ProviderName.Contains('\'') || spec.ProviderName.Contains('"'))
                continue;

            var provider = $"Provider[@Name='{spec.ProviderName}']";

            clauses.Add(spec.EventIds.Count == 0
                ? $"({provider})"
                : $"({provider} and ({string.Join(" or ", spec.EventIds.Select(id => $"EventID={id}"))}))");
        }

        // Deliberately null rather than "*": an empty clause list means the caller
        // gave us nothing usable, and matching every event in the log is a far
        // worse answer than matching none.
        if (clauses.Count == 0) return null;

        var timestamp = since.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        return $"*[System[({string.Join(" or ", clauses)}) " +
               $"and TimeCreated[@SystemTime>='{timestamp}']]]";
    }

    // ── Defensive field access ────────────────────────────────────────────────

    /// <summary>
    /// Rendered text, or null when it cannot be produced.
    ///
    /// FormatDescription throws when the publisher's message resources are
    /// missing — routine for third-party drivers and for publishers belonging to
    /// software that has since been uninstalled. The event is still worth
    /// keeping; only its prose is unavailable, which is why nothing in the
    /// classifier depends on it.
    /// </summary>
    private static string? SafeDescription(EventRecord record)
    {
        try
        {
            return record.FormatDescription();
        }
        catch (EventLogException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // Thrown for some malformed provider manifests.
            return null;
        }
    }

    /// <summary>
    /// Positional event data. Individual property reads can throw for the same
    /// manifest reasons as the description, so a failure yields an empty list
    /// rather than losing the event.
    /// </summary>
    private static IReadOnlyList<string?> SafeProperties(EventRecord record)
    {
        try
        {
            var properties = record.Properties;
            if (properties.Count == 0) return [];

            var values = new List<string?>(properties.Count);
            foreach (var property in properties)
                values.Add(property.Value?.ToString());

            return values;
        }
        catch (EventLogException)
        {
            return [];
        }
    }

    /// <summary>
    /// EventRecord timestamps come back as local <see cref="DateTime"/>, but with
    /// Unspecified kind on some records — which DateTimeOffset's constructor
    /// rejects. Treat unspecified as local, matching what Windows means.
    /// </summary>
    private static DateTimeOffset ToOffset(DateTime? timestamp)
    {
        if (timestamp is null) return DateTimeOffset.UtcNow;

        var value = timestamp.Value;

        return value.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local))
            : new DateTimeOffset(value);
    }
}
