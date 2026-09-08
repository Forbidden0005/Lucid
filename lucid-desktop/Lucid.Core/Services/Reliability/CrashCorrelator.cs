namespace Lucid.Services.Reliability;

/// <summary>
/// Turns a set of classified reliability events into ranked candidate
/// explanations.
///
/// This is the part that does the reasoning a mechanic does: one unexpected
/// shutdown is noise, four in a week is a pattern; a stop error on its own is a
/// fact, a stop error whose code points at power-state handling *alongside* a
/// storage fault is a story. Counting, clustering and corroboration happen here,
/// deterministically, so the answer does not depend on a language model getting
/// lucky.
///
/// Pure — no I/O, no clock. Findings carry their own evidence so nothing has to
/// be taken on trust, and every one carries a <see cref="FindingConfidence"/>
/// because these are inferences from log patterns, never established diagnoses.
/// </summary>
public static class CrashCorrelator
{
    /// <summary>Evidence events attached to each finding. Enough to justify it, not enough to bury it.</summary>
    private const int MaxEvidencePerFinding = 5;

    /// <summary>At or above this count, a repeated pattern is treated as established.</summary>
    private const int StrongPatternThreshold = 3;

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds findings from classified events. Returns them ordered by how much
    /// weight they deserve: confidence first, then how serious the kind of
    /// failure is, then how often it happened.
    /// </summary>
    public static IReadOnlyList<ReliabilityFinding> Correlate(IReadOnlyList<ReliabilityEvent> events)
    {
        if (events.Count == 0) return [];

        var findings = new List<ReliabilityFinding>();

        findings.AddRange(FindShutdownPattern(events));
        findings.AddRange(FindBugCheckPatterns(events));
        findings.AddRange(FindHardwarePattern(events));
        findings.AddRange(FindDiskPattern(events));
        findings.AddRange(FindSharedFaultingModule(events));
        findings.AddRange(FindApplicationPatterns(events));
        findings.AddRange(FindServicePattern(events));

        return findings
            .OrderByDescending(f => f.Confidence)
            .ThenByDescending(f => f.Occurrences)
            .ThenByDescending(f => f.LastSeen)
            .ToList();
    }

    // ── Unexpected shutdowns ──────────────────────────────────────────────────

    private static IEnumerable<ReliabilityFinding> FindShutdownPattern(IReadOnlyList<ReliabilityEvent> events)
    {
        var shutdowns = Of(events, ReliabilityEventKind.UnexpectedShutdown);
        if (shutdowns.Count == 0) yield break;

        // A shutdown corroborated by a stop error is a different story from one
        // that stands alone: the first points at software or hardware failing,
        // the second at power being cut.
        var withStopCode = shutdowns.Any(e => e.StopCode is not null)
                        || Of(events, ReliabilityEventKind.BugCheck).Count > 0;

        var confidence = (shutdowns.Count, withStopCode) switch
        {
            ( >= StrongPatternThreshold, _)    => FindingConfidence.High,
            ( >= 2, true)                      => FindingConfidence.High,
            ( >= 2, false)                     => FindingConfidence.Moderate,
            (_, true)                          => FindingConfidence.Moderate,
            _                                  => FindingConfidence.Low,
        };

        var explanation = withStopCode
            ? "The system stopped without shutting down cleanly, and Windows recorded a stop error around " +
              "the same time. That pattern points at something failing inside Windows or the hardware " +
              "rather than at power being interrupted."
            : "The system stopped without shutting down cleanly, and no stop error was recorded alongside it. " +
              "That usually means power was lost, the machine was reset, or it shut down too abruptly to " +
              "write a crash record. A loose power connection, a failing PSU, or overheating all produce " +
              "this pattern, and so does holding the power button.";

        yield return new ReliabilityFinding
        {
            Headline    = shutdowns.Count == 1
                              ? "One unexpected shutdown"
                              : $"{shutdowns.Count} unexpected shutdowns",
            Explanation = explanation,
            Confidence  = confidence,
            Occurrences = shutdowns.Count,
            LastSeen    = shutdowns[0].When,
            Evidence    = Cap(shutdowns),
            SuggestedChecks = withStopCode
                ?
                [
                    "Look at the stop codes below — they narrow this down more than anything else.",
                    "Check whether a driver or Windows update landed just before the first one.",
                ]
                :
                [
                    "Check CPU and GPU temperatures under load — thermal cutouts look exactly like this.",
                    "Reseat the power connectors, and try a different wall socket or power strip.",
                    "If the machine is on a UPS or a shared circuit, rule out brownouts first.",
                ],
        };
    }

    // ── Stop errors, grouped by code ──────────────────────────────────────────

    private static IEnumerable<ReliabilityFinding> FindBugCheckPatterns(IReadOnlyList<ReliabilityEvent> events)
    {
        var bugChecks = Of(events, ReliabilityEventKind.BugCheck);
        if (bugChecks.Count == 0) yield break;

        // Group by stop code: the same code repeating is a single problem, while
        // several different codes usually means something more general is wrong
        // (memory, storage, or power delivery) rather than one specific driver.
        var byCode = bugChecks
            .GroupBy(e => e.StopCode ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        foreach (var group in byCode)
        {
            var occurrences = group.Count();
            var code        = group.Key;
            var known       = StopCodeReference.Describe(code);

            var confidence = occurrences >= 2 || known is not null
                ? (occurrences >= StrongPatternThreshold ? FindingConfidence.High : FindingConfidence.Moderate)
                : FindingConfidence.Low;

            var explanation = known is not null
                ? $"Windows recorded {Times(occurrences)} with stop code {code}. {known.Meaning} " +
                  $"{known.CommonCauses}"
                : $"Windows recorded {Times(occurrences)} with stop code {code}. " +
                  "This code is not one of the common ones, so the crash dump is the next place to look.";

            yield return new ReliabilityFinding
            {
                Headline    = code == "unknown"
                                  ? $"{occurrences} stop error(s), code not recorded"
                                  : $"Stop error {code} — {occurrences}×",
                Explanation = explanation,
                Confidence  = confidence,
                Occurrences = occurrences,
                LastSeen    = group.Max(e => e.When),
                Evidence    = Cap(group.OrderByDescending(e => e.When).ToList()),
                SuggestedChecks = known?.Checks ??
                [
                    "Open the most recent dump in C:\\Windows\\Minidump to see which module faulted.",
                    "Note whether the crashes cluster around a particular activity — gaming, sleep, file copies.",
                ],
            };
        }

        // Several distinct codes is itself a signal, and a stronger one than any
        // single code in the set.
        if (byCode.Count >= 3)
        {
            yield return new ReliabilityFinding
            {
                Headline    = $"{byCode.Count} different stop codes",
                Explanation = "The stop errors are not all the same code. When crashes vary like this, the " +
                              "cause is more often something everything depends on — memory, the system " +
                              "drive, or power delivery — than one specific driver, because a single bad " +
                              "driver tends to fail the same way every time.",
                Confidence  = FindingConfidence.Moderate,
                Occurrences = bugChecks.Count,
                LastSeen    = bugChecks[0].When,
                Evidence    = Cap(bugChecks),
                SuggestedChecks =
                [
                    "Run Windows Memory Diagnostic, or MemTest86 overnight for a proper test.",
                    "Check the system drive's SMART health and run chkdsk.",
                    "If the CPU or RAM is overclocked or on an XMP/EXPO profile, try stock settings.",
                ],
            };
        }
    }

    // ── Hardware (WHEA) ───────────────────────────────────────────────────────

    private static IEnumerable<ReliabilityFinding> FindHardwarePattern(IReadOnlyList<ReliabilityEvent> events)
    {
        var hardware = Of(events, ReliabilityEventKind.HardwareError);
        if (hardware.Count == 0) yield break;

        // A WHEA record alongside 0x124 is about as strong as this kind of
        // inference gets: two independent subsystems reporting the same thing.
        var corroborated = Of(events, ReliabilityEventKind.BugCheck)
            .Any(e => e.StopCode?.Equals("0x00000124", StringComparison.OrdinalIgnoreCase) == true);

        var confidence = corroborated || hardware.Count >= StrongPatternThreshold
            ? FindingConfidence.High
            : FindingConfidence.Moderate;

        yield return new ReliabilityFinding
        {
            Headline    = $"Hardware-level errors logged ({hardware.Count})",
            Explanation = "WHEA is the layer where the CPU, memory and PCIe bus report faults they detected " +
                          "themselves, so these come from the hardware rather than from Windows interpreting " +
                          "something. Many WHEA entries are corrected errors the machine recovered from and " +
                          "are not urgent on their own — but they are worth taking seriously when they repeat, " +
                          "and more so when they line up with the crashes." +
                          (corroborated
                              ? " A 0x124 stop error was recorded as well, which is the stop code that " +
                                "corresponds directly to an uncorrected hardware error."
                              : string.Empty),
            Confidence  = confidence,
            Occurrences = hardware.Count,
            LastSeen    = hardware[0].When,
            Evidence    = Cap(hardware),
            SuggestedChecks =
            [
                "Test the memory — Windows Memory Diagnostic for a quick pass, MemTest86 for a real one.",
                "Remove any overclock or XMP/EXPO profile and see whether the errors stop.",
                "Check temperatures under load, and check the PSU if the machine is heavily loaded when it fails.",
            ],
        };
    }

    // ── Storage ───────────────────────────────────────────────────────────────

    private static IEnumerable<ReliabilityFinding> FindDiskPattern(IReadOnlyList<ReliabilityEvent> events)
    {
        var disk = Of(events, ReliabilityEventKind.DiskFault);
        if (disk.Count == 0) yield break;

        var confidence = disk.Count >= StrongPatternThreshold
            ? FindingConfidence.High
            : disk.Count >= 2 ? FindingConfidence.Moderate : FindingConfidence.Low;

        yield return new ReliabilityFinding
        {
            Headline    = $"Storage or filesystem faults logged ({disk.Count})",
            Explanation = "Windows logged errors from the storage stack — controller timeouts, unreadable " +
                          "sectors, or filesystem inconsistencies. A drive that intermittently stops " +
                          "responding can freeze or crash the whole system, because Windows is left waiting " +
                          "on a read that never returns. These entries are also worth attention on their own: " +
                          "they can precede data loss.",
            Confidence  = confidence,
            Occurrences = disk.Count,
            LastSeen    = disk[0].When,
            Evidence    = Cap(disk),
            SuggestedChecks =
            [
                "Check SMART health for the affected drive, particularly reallocated and pending sectors.",
                "Run chkdsk on the volume named in the events below.",
                "Reseat the SATA or NVMe connection — a marginal cable produces exactly these timeouts.",
                "Back up anything irreplaceable before troubleshooting further.",
            ],
        };
    }

    // ── A module faulting across several applications ─────────────────────────

    private static IEnumerable<ReliabilityFinding> FindSharedFaultingModule(IReadOnlyList<ReliabilityEvent> events)
    {
        var crashes = Of(events, ReliabilityEventKind.ApplicationCrash)
            .Where(e => e.Component is not null)
            .ToList();

        if (crashes.Count < 2) yield break;

        // One module faulting inside several unrelated applications is a much
        // stronger signal than any single application crashing, and it is the
        // kind of pattern that is invisible unless someone counts.
        var shared = crashes
            .GroupBy(e => e.Component!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Module = g.Key,
                Apps   = g.Select(e => e.ProcessName ?? "unknown")
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToList(),
                Events = g.OrderByDescending(e => e.When).ToList(),
            })
            .Where(x => x.Apps.Count >= 2)
            .OrderByDescending(x => x.Apps.Count);

        foreach (var group in shared)
        {
            yield return new ReliabilityFinding
            {
                Headline    = $"{group.Module} faulted in {group.Apps.Count} different applications",
                Explanation = $"The same module — {group.Module} — was named as the fault site in crashes " +
                              $"across {group.Apps.Count} unrelated applications " +
                              $"({string.Join(", ", group.Apps.Take(4))}). When one shared component fails " +
                              "in programs that have nothing else in common, the component is the better " +
                              "suspect than any of the programs. Graphics drivers, audio drivers and " +
                              "injected overlays show up this way.",
                Confidence  = group.Apps.Count >= 3 ? FindingConfidence.High : FindingConfidence.Moderate,
                Occurrences = group.Events.Count,
                LastSeen    = group.Events[0].When,
                Evidence    = Cap(group.Events),
                SuggestedChecks =
                [
                    $"Identify what ships {group.Module}, and update or reinstall it.",
                    "If it is a graphics driver, try a clean install of the current stable release.",
                    "If an overlay is involved (Discord, Steam, GeForce Experience, MSI Afterburner), disable it and retest.",
                ],
            };
        }
    }

    // ── Individual applications ───────────────────────────────────────────────

    private static IEnumerable<ReliabilityFinding> FindApplicationPatterns(IReadOnlyList<ReliabilityEvent> events)
    {
        var appEvents = events
            .Where(e => e.Kind is ReliabilityEventKind.ApplicationCrash
                                or ReliabilityEventKind.ApplicationHang)
            .Where(e => e.ProcessName is not null)
            .ToList();

        if (appEvents.Count == 0) yield break;

        var byApp = appEvents
            .GroupBy(e => e.ProcessName!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)          // a single crash is not a pattern
            .OrderByDescending(g => g.Count())
            .Take(5);                            // the long tail is noise, not findings

        foreach (var group in byApp)
        {
            var ordered = group.OrderByDescending(e => e.When).ToList();
            var modules = ordered.Select(e => e.Component)
                                 .Where(m => m is not null)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            yield return new ReliabilityFinding
            {
                Headline    = $"{group.Key} failed {ordered.Count} times",
                Explanation = $"{group.Key} crashed or stopped responding {ordered.Count} times in this " +
                              "window. An application failing repeatedly does not by itself explain the " +
                              "machine going down — a crashing program normally takes only itself with it. " +
                              "It matters here mainly as a clue: if it fails at the same moments the system " +
                              "does, both may be downstream of the same cause." +
                              (modules.Count > 0
                                  ? $" The fault was reported in {string.Join(", ", modules.Take(3))}."
                                  : string.Empty),
                Confidence  = ordered.Count >= StrongPatternThreshold
                                  ? FindingConfidence.Moderate
                                  : FindingConfidence.Low,
                Occurrences = ordered.Count,
                LastSeen    = ordered[0].When,
                Evidence    = Cap(ordered),
                SuggestedChecks =
                [
                    $"Check whether {group.Key} fails at the same times the system does.",
                    "Update or reinstall it, and check whether it has a known issue with your GPU driver.",
                ],
            };
        }
    }

    // ── Services ──────────────────────────────────────────────────────────────

    private static IEnumerable<ReliabilityFinding> FindServicePattern(IReadOnlyList<ReliabilityEvent> events)
    {
        var services = events
            .Where(e => e.Kind is ReliabilityEventKind.ServiceFailure
                                or ReliabilityEventKind.DriverFault)
            .ToList();

        if (services.Count < 2) yield break;

        var names = services.Select(e => e.Component)
                            .Where(c => c is not null)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(4)
                            .ToList();

        yield return new ReliabilityFinding
        {
            Headline    = $"Services or drivers failing ({services.Count})",
            Explanation = "Windows services terminated unexpectedly or drivers failed to load." +
                          (names.Count > 0 ? $" Affected: {string.Join(", ", names)}." : string.Empty) +
                          " On its own this is often harmless — Windows restarts most services " +
                          "automatically. It is worth reviewing because a driver that repeatedly fails to " +
                          "load can leave hardware in a state that destabilises the system.",
            Confidence  = services.Count >= StrongPatternThreshold
                              ? FindingConfidence.Moderate
                              : FindingConfidence.Low,
            Occurrences = services.Count,
            LastSeen    = services[0].When,
            Evidence    = Cap(services),
            SuggestedChecks =
            [
                "Check whether the affected drivers have a newer version available.",
                "Look for a recent Windows update or driver install just before the failures started.",
            ],
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Events of one kind, newest first.</summary>
    private static List<ReliabilityEvent> Of(IReadOnlyList<ReliabilityEvent> events, ReliabilityEventKind kind) =>
        events.Where(e => e.Kind == kind).OrderByDescending(e => e.When).ToList();

    private static IReadOnlyList<ReliabilityEvent> Cap(IReadOnlyList<ReliabilityEvent> events) =>
        events.Count <= MaxEvidencePerFinding ? events : events.Take(MaxEvidencePerFinding).ToList();

    private static string Times(int count) =>
        count == 1 ? "one stop error" : $"{count} stop errors";
}
