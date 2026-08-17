using System.Text.RegularExpressions;

namespace Lucid.Services.Reliability;

/// <summary>
/// Turns raw Windows event records into classified <see cref="ReliabilityEvent"/>s.
///
/// Pure and deterministic — no I/O, no clock, no state. Everything Lucid
/// understands about what a given event ID *means* lives here, which is what
/// makes the interpretation testable against constructed records rather than
/// requiring a machine that has genuinely crashed.
///
/// Two deliberate rules:
///
///   • Summaries describe what Windows recorded, never what caused it. "Windows
///     recorded a stop error (0x0000009F)" is a fact; "your storage driver is
///     failing" is an inference, and inferences belong in
///     <see cref="CrashCorrelator"/> where they carry a confidence.
///
///   • A severe event from a known publisher is surfaced even when the specific
///     event ID is unrecognised. Silently discarding a Critical-level event
///     because this file has not heard of it is how a real cause gets missed.
/// </summary>
public static class ReliabilityEventClassifier
{
    // ── Publishers worth querying ─────────────────────────────────────────────
    // Used both to filter the event-log query and to decide whether an
    // unrecognised event ID is still worth surfacing.

    /// <summary>
    /// System-log publishers that report machine-level instability, with the
    /// event IDs worth reading from each.
    ///
    /// Publishers listed without IDs are read wholesale because everything they
    /// log is relevant. Publishers with IDs are narrowed deliberately — see
    /// EventQuerySpec for why that matters for volume.
    /// </summary>
    public static readonly IReadOnlyList<EventQuerySpec> SystemQuery =
    [
        // The machine stopped without a clean shutdown.
        new() { ProviderName = "Microsoft-Windows-Kernel-Power", EventIds = [41] },
        new() { ProviderName = "EventLog",                       EventIds = [6008] },

        // Stop errors. Low volume, so read whole.
        new() { ProviderName = "BugCheck" },
        new() { ProviderName = "Microsoft-Windows-WER-SystemErrorReporting" },

        // Hardware faults. Every WHEA entry is meaningful.
        new() { ProviderName = "Microsoft-Windows-WHEA-Logger" },

        // Storage and filesystem. Narrowed: these publishers also log routine chatter.
        new() { ProviderName = "disk",     EventIds = [7, 11, 51, 52, 153] },
        new() { ProviderName = "Ntfs",     EventIds = [55, 137, 140] },
        new() { ProviderName = "volmgr",   EventIds = [46, 49] },
        new() { ProviderName = "storahci", EventIds = [129] },
        new() { ProviderName = "stornvme", EventIds = [129] },

        // Services and drivers. 7036 (state changed) is excluded on purpose —
        // it fires constantly and says nothing about reliability.
        new() { ProviderName = "Service Control Manager", EventIds = [7000, 7026, 7031, 7034] },
        new() { ProviderName = "Microsoft-Windows-Kernel-PnP", EventIds = [219, 411] },
    ];

    /// <summary>Application-log publishers that report application-level failures.</summary>
    public static readonly IReadOnlyList<EventQuerySpec> ApplicationQuery =
    [
        new() { ProviderName = "Application Error",       EventIds = [1000] },
        new() { ProviderName = "Application Hang",        EventIds = [1002] },
        new() { ProviderName = "Windows Error Reporting", EventIds = [1001] },
    ];

    // Stop codes appear in the rendered text rather than as a clean property on
    // most systems, so they are extracted by pattern. Matches both the 8-digit
    // (32-bit) and 16-digit (64-bit) renderings.
    private static readonly Regex StopCodePattern =
        new(@"0x[0-9A-Fa-f]{8,16}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ── Classification ────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies one raw record. Returns null when the record carries no
    /// reliability signal and should be ignored.
    /// </summary>
    public static ReliabilityEvent? Classify(RawEventRecord record)
    {
        var kind = DetermineKind(record);
        if (kind is null) return null;

        return new ReliabilityEvent
        {
            Kind         = kind.Value,
            When         = record.TimeCreated,
            ProviderName = record.ProviderName,
            EventId      = record.EventId,
            Level        = record.Level,
            StopCode     = ExtractStopCode(record, kind.Value),
            Component    = ExtractComponent(record, kind.Value),
            ProcessName  = ExtractProcessName(record, kind.Value),
            Summary      = BuildSummary(record, kind.Value),
        };
    }

    /// <summary>Classifies a batch, dropping records with no reliability signal.</summary>
    public static IReadOnlyList<ReliabilityEvent> ClassifyAll(IEnumerable<RawEventRecord> records)
    {
        var results = new List<ReliabilityEvent>();

        foreach (var record in records)
        {
            var classified = Classify(record);
            if (classified is not null) results.Add(classified);
        }

        return results;
    }

    // ── Kind resolution ───────────────────────────────────────────────────────

    private static ReliabilityEventKind? DetermineKind(RawEventRecord record)
    {
        var provider = record.ProviderName;

        // ── Machine stopped without a clean shutdown ──
        if (Is(provider, "Microsoft-Windows-Kernel-Power") && record.EventId == 41)
            return ReliabilityEventKind.UnexpectedShutdown;

        if (Is(provider, "EventLog") && record.EventId == 6008)
            return ReliabilityEventKind.UnexpectedShutdown;

        // ── Stop error / blue screen ──
        if (Is(provider, "BugCheck") && record.EventId == 1001)
            return ReliabilityEventKind.BugCheck;

        if (Is(provider, "Microsoft-Windows-WER-SystemErrorReporting") && record.EventId == 1001)
            return ReliabilityEventKind.BugCheck;

        // ── Hardware-level error ──
        // WHEA reports corrected and uncorrected errors alike; the correlator
        // weighs severity, so every WHEA event is kept here.
        if (Is(provider, "Microsoft-Windows-WHEA-Logger"))
            return ReliabilityEventKind.HardwareError;

        // ── Storage and filesystem ──
        if (Is(provider, "disk") || Is(provider, "Ntfs") ||
            Is(provider, "volmgr") || Is(provider, "storahci") || Is(provider, "stornvme"))
            return ReliabilityEventKind.DiskFault;

        // ── Driver load failures ──
        if (Is(provider, "Microsoft-Windows-Kernel-PnP") && record.EventId is 219 or 411)
            return ReliabilityEventKind.DriverFault;

        // ── Services ──
        if (Is(provider, "Service Control Manager"))
        {
            // 7031/7034 are unexpected terminations; 7000/7026 are load failures,
            // which are driver problems wearing a service's clothes.
            if (record.EventId is 7031 or 7034) return ReliabilityEventKind.ServiceFailure;
            if (record.EventId is 7000 or 7026) return ReliabilityEventKind.DriverFault;
            return record.Level <= 2 ? ReliabilityEventKind.ServiceFailure : null;
        }

        // ── Applications ──
        if (Is(provider, "Application Error"))
            return record.EventId == 1000 ? ReliabilityEventKind.ApplicationCrash : null;

        if (Is(provider, "Application Hang"))
            return record.EventId == 1002 ? ReliabilityEventKind.ApplicationHang : null;

        if (Is(provider, "Windows Error Reporting"))
        {
            // WER buckets both user-mode crashes and LiveKernelEvents. The text
            // is the only reliable discriminator, and it may be absent.
            if (record.Message?.Contains("LiveKernel", StringComparison.OrdinalIgnoreCase) == true)
                return ReliabilityEventKind.BugCheck;

            return record.Level <= 2 ? ReliabilityEventKind.ApplicationCrash : null;
        }

        // ── Boot ──
        if (Is(provider, "Microsoft-Windows-Kernel-Boot"))
            return record.Level <= 2 ? ReliabilityEventKind.Other : null;

        // Unrecognised ID from a publisher we deliberately queried: keep it if
        // Windows itself called it Critical or Error. Better a vague entry the
        // user can inspect than a silently dropped cause.
        return record.Level <= 2 ? ReliabilityEventKind.Other : null;
    }

    // ── Detail extraction ─────────────────────────────────────────────────────

    private static string? ExtractStopCode(RawEventRecord record, ReliabilityEventKind kind)
    {
        if (kind != ReliabilityEventKind.BugCheck &&
            kind != ReliabilityEventKind.UnexpectedShutdown) return null;

        // Kernel-Power 41 carries the bugcheck code as a property when the
        // shutdown followed a stop error; it is 0 for a plain power loss.
        foreach (var property in record.Properties)
        {
            if (string.IsNullOrWhiteSpace(property)) continue;

            var match = StopCodePattern.Match(property);
            if (match.Success && !IsZeroCode(match.Value)) return Normalize(match.Value);
        }

        if (record.Message is null) return null;

        var fromMessage = StopCodePattern.Match(record.Message);
        return fromMessage.Success && !IsZeroCode(fromMessage.Value)
            ? Normalize(fromMessage.Value)
            : null;
    }

    private static string? ExtractComponent(RawEventRecord record, ReliabilityEventKind kind) => kind switch
    {
        // Application Error 1000 property order is fixed by the publisher:
        // 0 app name, 1 app version, 2 app timestamp, 3 faulting module.
        ReliabilityEventKind.ApplicationCrash => Property(record, 3),

        // SCM names the service in the first property.
        ReliabilityEventKind.ServiceFailure => Property(record, 0),

        ReliabilityEventKind.DriverFault => Property(record, 0),

        // Storage events name the device rather than a module.
        ReliabilityEventKind.DiskFault => Property(record, 0) ?? record.ProviderName,

        _ => null,
    };

    private static string? ExtractProcessName(RawEventRecord record, ReliabilityEventKind kind) => kind switch
    {
        ReliabilityEventKind.ApplicationCrash or
        ReliabilityEventKind.ApplicationHang => Property(record, 0),
        _                                    => null,
    };

    // ── Summaries ─────────────────────────────────────────────────────────────
    //    Factual descriptions of what was logged. No causal claims.

    private static string BuildSummary(RawEventRecord record, ReliabilityEventKind kind)
    {
        var app       = ExtractProcessName(record, kind);
        var component = ExtractComponent(record, kind);
        var stopCode  = ExtractStopCode(record, kind);

        return kind switch
        {
            ReliabilityEventKind.UnexpectedShutdown when stopCode is not null =>
                $"The system restarted without shutting down cleanly, following a stop error ({stopCode}).",

            ReliabilityEventKind.UnexpectedShutdown =>
                "The system restarted without shutting down cleanly — power loss, a hard reset, or a stop error.",

            ReliabilityEventKind.BugCheck when stopCode is not null =>
                $"Windows recorded a stop error with code {stopCode}.",

            ReliabilityEventKind.BugCheck =>
                "Windows recorded a stop error.",

            ReliabilityEventKind.HardwareError =>
                $"WHEA logged a hardware-level error (event {record.EventId}).",

            ReliabilityEventKind.DiskFault when component is not null =>
                $"A storage or filesystem fault was logged for {component}.",

            ReliabilityEventKind.DiskFault =>
                "A storage or filesystem fault was logged.",

            ReliabilityEventKind.ApplicationCrash when app is not null && component is not null =>
                $"{app} closed unexpectedly, with the fault reported in {component}.",

            ReliabilityEventKind.ApplicationCrash when app is not null =>
                $"{app} closed unexpectedly.",

            ReliabilityEventKind.ApplicationCrash =>
                "An application closed unexpectedly.",

            ReliabilityEventKind.ApplicationHang when app is not null =>
                $"{app} stopped responding.",

            ReliabilityEventKind.ApplicationHang =>
                "An application stopped responding.",

            ReliabilityEventKind.ServiceFailure when component is not null =>
                $"The {component} service terminated unexpectedly.",

            ReliabilityEventKind.ServiceFailure =>
                "A Windows service terminated unexpectedly.",

            ReliabilityEventKind.DriverFault when component is not null =>
                $"A driver or service failed to start: {component}.",

            ReliabilityEventKind.DriverFault =>
                "A driver failed to load.",

            // Unrecognised but severe — say exactly that, and show the raw text.
            _ => FirstLine(record.Message)
                 ?? $"{record.ProviderName} logged a {LevelName(record.Level)} event (ID {record.EventId}).",
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool Is(string provider, string expected) =>
        string.Equals(provider, expected, StringComparison.OrdinalIgnoreCase);

    private static string? Property(RawEventRecord record, int index) =>
        index >= 0 && index < record.Properties.Count &&
        !string.IsNullOrWhiteSpace(record.Properties[index])
            ? record.Properties[index]!.Trim()
            : null;

    /// <summary>
    /// A code of all zeros means "no stop error" — Kernel-Power 41 uses it for an
    /// ordinary power loss, and reporting it as a stop code would invent a crash
    /// that never happened.
    /// </summary>
    private static bool IsZeroCode(string code) =>
        code.AsSpan(2).TrimStart('0').IsEmpty;

    private static string Normalize(string code) =>
        "0x" + code.AsSpan(2).ToString().ToUpperInvariant();

    /// <summary>
    /// Event text runs to paragraphs. Only the first line is a summary; the rest
    /// is boilerplate about where to find more information.
    /// </summary>
    private static string? FirstLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        var line = message.AsSpan();
        var end  = line.IndexOfAny('\r', '\n');
        if (end >= 0) line = line[..end];

        var trimmed = line.Trim().ToString();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string LevelName(byte level) => level switch
    {
        1 => "critical",
        2 => "error",
        3 => "warning",
        _ => "informational",
    };
}
