namespace Lucid.Services.Reliability;

// ─────────────────────────────────────────────────────────────────────────────
// Reliability domain — what the Windows event logs say about this machine's
// stability history.
//
// This is the data source behind "why does my PC keep crashing". Until this
// existed, Lucid could see the machine's present (telemetry, processes) but had
// no access to its past failures, so the best it could do was reason from
// current resource usage and suggest the user open Event Viewer themselves.
//
// Design: reading the event log is I/O against a Windows API, so it sits behind
// IWindowsEventLogReader. Everything that *interprets* what was read is a pure
// function over RawEventRecord, which is what makes this domain testable
// without a Windows event log to read from.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One event exactly as it came out of the Windows event log, before any
/// interpretation. Deliberately dumb: the classifier decides what it means.
/// </summary>
public sealed record RawEventRecord
{
    /// <summary>"System", "Application", …</summary>
    public required string LogName { get; init; }

    /// <summary>Publisher, e.g. "Microsoft-Windows-Kernel-Power", "Application Error".</summary>
    public required string ProviderName { get; init; }

    public required int EventId { get; init; }

    /// <summary>Windows event level: 1 Critical, 2 Error, 3 Warning, 4 Information.</summary>
    public required byte Level { get; init; }

    public required DateTimeOffset TimeCreated { get; init; }

    /// <summary>
    /// Rendered description. Null when the publisher's message resources are not
    /// installed — common on stripped systems, so nothing may depend on it.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Positional event data. Provider-specific and frequently the only reliable
    /// source for details like a faulting module, since <see cref="Message"/> is
    /// localised and sometimes absent.
    /// </summary>
    public IReadOnlyList<string?> Properties { get; init; } = [];
}

/// <summary>
/// What kind of reliability problem an event represents.
///
/// Ordered roughly by how strongly it indicates instability, which the
/// correlator uses when ranking candidate causes.
/// </summary>
public enum ReliabilityEventKind
{
    /// <summary>Recognised as a reliability event but not one of the kinds below.</summary>
    Other = 0,

    /// <summary>The machine stopped without a clean shutdown (Kernel-Power 41, EventLog 6008).</summary>
    UnexpectedShutdown = 1,

    /// <summary>A stop error / blue screen, usually carrying a stop code (BugCheck 1001).</summary>
    BugCheck = 2,

    /// <summary>Hardware-level error reported by WHEA — CPU, memory, PCIe, or bus.</summary>
    HardwareError = 3,

    /// <summary>Storage or filesystem fault — bad sectors, controller resets, NTFS corruption.</summary>
    DiskFault = 4,

    /// <summary>An application terminated unexpectedly (Application Error 1000).</summary>
    ApplicationCrash = 5,

    /// <summary>An application stopped responding (Application Hang 1002).</summary>
    ApplicationHang = 6,

    /// <summary>A Windows service terminated unexpectedly (SCM 7031 / 7034).</summary>
    ServiceFailure = 7,

    /// <summary>A driver reported a failure or failed to load.</summary>
    DriverFault = 8,
}

/// <summary>
/// A classified reliability event: a raw log record plus what Lucid understands
/// about it.
/// </summary>
public sealed record ReliabilityEvent
{
    public required ReliabilityEventKind Kind        { get; init; }
    public required DateTimeOffset       When        { get; init; }
    public required string               ProviderName { get; init; }
    public required int                  EventId     { get; init; }

    /// <summary>
    /// One-line plain-English description, written to be read by a person.
    /// Never asserts a cause — that is the correlator's job, with a confidence
    /// attached.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>Bugcheck stop code (e.g. "0x0000009F"), when the event carries one.</summary>
    public string? StopCode { get; init; }

    /// <summary>
    /// Component the event blames: a faulting module for an app crash, a device
    /// for a disk fault, a service name for a service failure. Null when the
    /// event does not name one.
    /// </summary>
    public string? Component { get; init; }

    /// <summary>Application or service the event concerns, when it names one.</summary>
    public string? ProcessName { get; init; }

    /// <summary>True for events Windows itself marked Critical or Error.</summary>
    public bool IsSevere => Level <= 2;

    /// <summary>Windows event level, carried through for filtering and display.</summary>
    public required byte Level { get; init; }
}

// ── Correlation output ───────────────────────────────────────────────────────

/// <summary>
/// How strongly the evidence supports a candidate explanation.
///
/// Deliberately coarse and never certain: these bands are what the UI and the
/// language model are given, and they exist so a finding can never be reported
/// as established fact when it is an inference from log patterns.
/// </summary>
public enum FindingConfidence
{
    /// <summary>A single event, or a pattern with an innocent explanation. Worth a look.</summary>
    Low = 0,

    /// <summary>A repeated pattern, or one corroborated by a second source.</summary>
    Moderate = 1,

    /// <summary>A consistent, repeated pattern with corroborating evidence.</summary>
    High = 2,
}

/// <summary>
/// One candidate explanation for the machine's instability, with the events it
/// was drawn from so the user can always see why Lucid is saying it.
/// </summary>
public sealed record ReliabilityFinding
{
    /// <summary>Short headline, e.g. "Repeated unexpected shutdowns".</summary>
    public required string Headline { get; init; }

    /// <summary>
    /// What this pattern means and what it does not mean. Written in
    /// confidence-aware language — describes what was observed and what would
    /// explain it, rather than declaring a diagnosis.
    /// </summary>
    public required string Explanation { get; init; }

    public required FindingConfidence Confidence { get; init; }

    /// <summary>How many events in the window support this finding.</summary>
    public required int Occurrences { get; init; }

    /// <summary>Most recent supporting event.</summary>
    public required DateTimeOffset LastSeen { get; init; }

    /// <summary>
    /// The events this finding was built from, newest first and capped, so the
    /// reasoning is always inspectable rather than asserted.
    /// </summary>
    public IReadOnlyList<ReliabilityEvent> Evidence { get; init; } = [];

    /// <summary>
    /// What a person could do next to narrow this down further. Suggestions
    /// only — nothing here is executed automatically.
    /// </summary>
    public IReadOnlyList<string> SuggestedChecks { get; init; } = [];
}

/// <summary>
/// The result of looking into a machine's stability over a time window.
/// </summary>
public sealed record ReliabilityReport
{
    /// <summary>Start of the window examined.</summary>
    public required DateTimeOffset Since { get; init; }

    /// <summary>When this report was produced.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Candidate explanations, most confident and most severe first.</summary>
    public IReadOnlyList<ReliabilityFinding> Findings { get; init; } = [];

    /// <summary>Every classified event in the window, newest first.</summary>
    public IReadOnlyList<ReliabilityEvent> Events { get; init; } = [];

    /// <summary>
    /// True when the event log could not be read at all — most often because the
    /// log is empty, cleared, or access was denied. Distinguishes "nothing went
    /// wrong" from "could not look", which must never be reported as the same
    /// thing.
    /// </summary>
    public bool ReadFailed { get; init; }

    /// <summary>Why the read failed, when it did. Shown to the user verbatim.</summary>
    public string? ReadFailureReason { get; init; }

    /// <summary>True when the window contained no reliability events at all.</summary>
    public bool IsClean => !ReadFailed && Events.Count == 0;
}
