using System.Globalization;

namespace Lucid.Services.Reliability;

/// <summary>
/// What Windows stop codes mean, in plain English.
///
/// This is deliberately a static reference table rather than something a
/// language model is asked to recall. A stop code is a fixed, documented fact
/// about Windows: 0x133 is DPC_WATCHDOG_VIOLATION whether or not a 3-billion
/// parameter model happens to remember it. Encoding it here means the
/// explanation is correct every time, on any model, with no network.
///
/// Entries describe what the code indicates and what commonly produces it —
/// hedged, because a stop code narrows the field, it does not name the culprit.
/// The dump file does that, and the checks point there.
/// </summary>
public static class StopCodeReference
{
    /// <summary>What a recognised stop code tells you.</summary>
    public sealed record StopCodeInfo
    {
        /// <summary>Windows' own name for the code, e.g. "DPC_WATCHDOG_VIOLATION".</summary>
        public required string Name { get; init; }

        /// <summary>One or two sentences on what the code indicates. Leads with the name.</summary>
        public required string Meaning { get; init; }

        /// <summary>What commonly produces it, phrased as likelihoods rather than verdicts.</summary>
        public required string CommonCauses { get; init; }

        /// <summary>Concrete next checks. Suggestions only — nothing here runs automatically.</summary>
        public required IReadOnlyList<string> Checks { get; init; }
    }

    /// <summary>
    /// Looks up a stop code. Accepts any hex rendering ("0x9F", "0x0000009F",
    /// "0X9f") because event text is not consistent about width or case.
    /// Returns null for codes not in the table.
    /// </summary>
    public static StopCodeInfo? Describe(string? stopCode)
    {
        var value = Parse(stopCode);
        return value is null ? null : Table.GetValueOrDefault(value.Value);
    }

    /// <summary>Parses a hex stop code to its numeric value, or null if unparseable.</summary>
    public static ulong? Parse(string? stopCode)
    {
        if (string.IsNullOrWhiteSpace(stopCode)) return null;

        var text = stopCode.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (text.Length == 0) return null;

        return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    // ── The table ─────────────────────────────────────────────────────────────
    //    Keyed numerically so width and case never matter.

    private static readonly Dictionary<ulong, StopCodeInfo> Table = new()
    {
        [0x0A] = new()
        {
            Name         = "IRQL_NOT_LESS_OR_EQUAL",
            Meaning      = "That code is IRQL_NOT_LESS_OR_EQUAL — kernel code touched memory it should not have, at a point where it was not allowed to.",
            CommonCauses = "Almost always a driver bug, and occasionally faulty RAM. Recently updated or unsigned drivers are the usual starting point.",
            Checks =
            [
                "Open the newest dump in C:\\Windows\\Minidump and note the module named.",
                "Roll back or update whatever driver you changed most recently.",
                "Run Windows Memory Diagnostic to rule out RAM.",
            ],
        },
        [0x1A] = new()
        {
            Name         = "MEMORY_MANAGEMENT",
            Meaning      = "That code is MEMORY_MANAGEMENT — Windows found its memory bookkeeping in a state that should be impossible.",
            CommonCauses = "Frequently genuinely faulty RAM or an unstable memory overclock. Storage problems and driver bugs can also produce it.",
            Checks =
            [
                "Test RAM properly — MemTest86 overnight, not just the built-in quick pass.",
                "Drop any XMP/EXPO profile to stock speeds and see whether it stops.",
                "If you have multiple sticks, test them one at a time.",
            ],
        },
        [0x1E] = new()
        {
            Name         = "KMODE_EXCEPTION_NOT_HANDLED",
            Meaning      = "That code is KMODE_EXCEPTION_NOT_HANDLED — kernel code hit an error it had no handler for.",
            CommonCauses = "Usually a driver. The dump normally names the module directly.",
            Checks = ["Open the newest dump in C:\\Windows\\Minidump and note the faulting module."],
        },
        [0x24] = new()
        {
            Name         = "NTFS_FILE_SYSTEM",
            Meaning      = "That code is NTFS_FILE_SYSTEM — the crash happened inside the NTFS filesystem driver.",
            CommonCauses = "Points at the drive or its connection more often than at Windows: filesystem corruption, bad sectors, or a marginal cable.",
            Checks =
            [
                "Run chkdsk /f on the system drive.",
                "Check SMART health, particularly reallocated and pending sector counts.",
                "Reseat the SATA or NVMe connection.",
            ],
        },
        [0x3B] = new()
        {
            Name         = "SYSTEM_SERVICE_EXCEPTION",
            Meaning      = "That code is SYSTEM_SERVICE_EXCEPTION — something went wrong crossing from user mode into the kernel.",
            CommonCauses = "Commonly graphics drivers, and anything that hooks the system such as overlays or anti-cheat.",
            Checks =
            [
                "Clean-install the current stable graphics driver.",
                "Disable in-game overlays (Discord, Steam, GeForce Experience) and retest.",
            ],
        },
        [0x50] = new()
        {
            Name         = "PAGE_FAULT_IN_NONPAGED_AREA",
            Meaning      = "That code is PAGE_FAULT_IN_NONPAGED_AREA — Windows asked for memory that should always be present and it was not there.",
            CommonCauses = "Faulty RAM is high on the list, along with driver bugs and, less often, a failing drive.",
            Checks =
            [
                "Test RAM with MemTest86.",
                "Remove any memory overclock.",
                "Check the newest dump for a named module.",
            ],
        },
        [0x7A] = new()
        {
            Name         = "KERNEL_DATA_INPAGE_ERROR",
            Meaning      = "That code is KERNEL_DATA_INPAGE_ERROR — Windows tried to read a page from disk and the read failed.",
            CommonCauses = "Strongly suggests storage: bad sectors, a failing drive, or a controller/cable fault. Occasionally RAM.",
            Checks =
            [
                "Check SMART health for the system drive now, and back up anything irreplaceable first.",
                "Run chkdsk /r.",
                "Reseat or replace the drive cable.",
            ],
        },
        [0x7E] = new()
        {
            Name         = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
            Meaning      = "That code is SYSTEM_THREAD_EXCEPTION_NOT_HANDLED — a system thread hit an error nothing handled.",
            CommonCauses = "Usually a driver, and graphics drivers are the most common single source.",
            Checks = ["Clean-install the graphics driver.", "Check the dump for the faulting module."],
        },
        [0x9C] = new()
        {
            Name         = "MACHINE_CHECK_EXCEPTION",
            Meaning      = "That code is MACHINE_CHECK_EXCEPTION — the CPU itself reported an unrecoverable error.",
            CommonCauses = "This one comes from the hardware rather than from Windows. Unstable overclocks, insufficient or failing power delivery, overheating, or a genuine CPU fault.",
            Checks =
            [
                "Remove all overclocks, including CPU, memory and any undervolt curve.",
                "Check CPU temperatures under sustained load.",
                "Check the PSU, especially if it is older or heavily loaded.",
            ],
        },
        [0x9F] = new()
        {
            Name         = "DRIVER_POWER_STATE_FAILURE",
            Meaning      = "That code is DRIVER_POWER_STATE_FAILURE — a driver failed to complete a power transition, so sleep, hibernate or wake did not finish.",
            CommonCauses = "Network, storage and graphics drivers are the usual candidates. Often reproducible around sleep and wake rather than under load.",
            Checks =
            [
                "Note whether the crashes happen around sleep, wake or shutdown.",
                "Update network, storage and graphics drivers.",
                "Try disabling fast startup, and turn off USB selective suspend.",
            ],
        },
        [0xBE] = new()
        {
            Name         = "ATTEMPTED_WRITE_TO_READONLY_MEMORY",
            Meaning      = "That code is ATTEMPTED_WRITE_TO_READONLY_MEMORY — a driver wrote to memory it was only allowed to read.",
            CommonCauses = "A driver bug. The dump normally names it.",
            Checks = ["Check the newest dump for the module, then update or remove that driver."],
        },
        [0xC5] = new()
        {
            Name         = "DRIVER_CORRUPTED_EXPOOL",
            Meaning      = "That code is DRIVER_CORRUPTED_EXPOOL — a driver wrote outside its own memory and corrupted the kernel pool.",
            CommonCauses = "A driver bug, and sometimes faulty RAM.",
            Checks = ["Run Driver Verifier to catch the offender.", "Test RAM."],
        },
        [0xEF] = new()
        {
            Name         = "CRITICAL_PROCESS_DIED",
            Meaning      = "That code is CRITICAL_PROCESS_DIED — a process Windows cannot run without stopped.",
            CommonCauses = "Corrupted system files, a failing drive, or occasionally third-party software interfering with Windows components.",
            Checks =
            [
                "Run sfc /scannow, then DISM /Online /Cleanup-Image /RestoreHealth.",
                "Check the system drive's health.",
            ],
        },
        [0xF4] = new()
        {
            Name         = "CRITICAL_OBJECT_TERMINATION",
            Meaning      = "That code is CRITICAL_OBJECT_TERMINATION — a critical system object ended unexpectedly.",
            CommonCauses = "Frequently storage-related: Windows lost access to something it needed. Also corrupted system files.",
            Checks = ["Check SMART health and cabling.", "Run sfc /scannow."],
        },
        [0xFC] = new()
        {
            Name         = "ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY",
            Meaning      = "That code is ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY — code tried to run from memory marked non-executable.",
            CommonCauses = "A driver bug, occasionally faulty RAM.",
            Checks = ["Check the dump for the module.", "Test RAM."],
        },
        [0x101] = new()
        {
            Name         = "CLOCK_WATCHDOG_TIMEOUT",
            Meaning      = "That code is CLOCK_WATCHDOG_TIMEOUT — one CPU core stopped responding to the others.",
            CommonCauses = "Very often an unstable CPU overclock or undervolt. Also insufficient power delivery, overheating, or a BIOS problem.",
            Checks =
            [
                "Remove all CPU overclocks and undervolts, including per-core curves.",
                "Update the BIOS.",
                "Check CPU temperatures and PSU capacity under load.",
            ],
        },
        [0x109] = new()
        {
            Name         = "CRITICAL_STRUCTURE_CORRUPTION",
            Meaning      = "That code is CRITICAL_STRUCTURE_CORRUPTION — a kernel structure was modified in a way that should not be possible.",
            CommonCauses = "Faulty RAM, an unstable overclock, or software patching the kernel — anti-cheat and older tuning utilities do this.",
            Checks = ["Test RAM.", "Remove overclocks.", "Uninstall kernel-level tuning or monitoring tools and retest."],
        },
        [0x117] = new()
        {
            Name         = "VIDEO_TDR_TIMEOUT",
            Meaning      = "That code is VIDEO_TDR_TIMEOUT — the graphics driver stopped responding and Windows could not reset it.",
            CommonCauses = "Graphics driver problems, GPU overheating, an unstable GPU overclock, or a PSU that cannot hold up under GPU load spikes.",
            Checks =
            [
                "Clean-install the graphics driver with DDU, then the current stable release.",
                "Check GPU temperatures, and remove any GPU overclock.",
                "Reseat the GPU power connectors.",
            ],
        },
        [0x124] = new()
        {
            Name         = "WHEA_UNCORRECTABLE_ERROR",
            Meaning      = "That code is WHEA_UNCORRECTABLE_ERROR — the hardware reported a fault it could not correct, and Windows stopped rather than continue.",
            CommonCauses = "This is a hardware-side report, not Windows' interpretation. Unstable overclocks, power delivery, overheating, failing RAM, or a genuine CPU or motherboard fault.",
            Checks =
            [
                "Remove every overclock and undervolt, and set memory to stock.",
                "Check temperatures under sustained load.",
                "Test RAM with MemTest86.",
                "Check the PSU — this code is common on units that are ageing or undersized.",
            ],
        },
        [0x133] = new()
        {
            Name         = "DPC_WATCHDOG_VIOLATION",
            Meaning      = "That code is DPC_WATCHDOG_VIOLATION — a driver held the CPU far longer than it is allowed to without yielding.",
            CommonCauses = "Very commonly a storage driver, and older SSD firmware is a repeat offender. Network drivers and outdated chipset drivers also produce it.",
            Checks =
            [
                "Check for SSD firmware updates from the drive manufacturer.",
                "Update the storage controller and chipset drivers.",
                "Make sure the SATA controller is not running a generic Microsoft driver where a vendor one exists.",
            ],
        },
        [0x139] = new()
        {
            Name         = "KERNEL_SECURITY_CHECK_FAILURE",
            Meaning      = "That code is KERNEL_SECURITY_CHECK_FAILURE — a kernel integrity check found a corrupted data structure.",
            CommonCauses = "Driver bugs, faulty RAM, or filesystem corruption.",
            Checks = ["Test RAM.", "Run chkdsk and sfc /scannow.", "Check the dump for a named driver."],
        },
        [0x13A] = new()
        {
            Name         = "KERNEL_MODE_HEAP_CORRUPTION",
            Meaning      = "That code is KERNEL_MODE_HEAP_CORRUPTION — the kernel heap was found in a corrupted state.",
            CommonCauses = "A driver bug, occasionally faulty RAM.",
            Checks = ["Run Driver Verifier.", "Test RAM."],
        },
        [0x144] = new()
        {
            Name         = "BUGCODE_USB3_DRIVER",
            Meaning      = "That code is BUGCODE_USB3_DRIVER — the crash happened inside the USB 3 stack.",
            CommonCauses = "A USB device or its driver, or a USB controller driver. Docks and hubs are frequent culprits.",
            Checks =
            [
                "Unplug USB devices one at a time to find which one triggers it.",
                "Update chipset and USB controller drivers.",
                "Disconnect docks and hubs and retest.",
            ],
        },
        [0x154] = new()
        {
            Name         = "UNEXPECTED_STORE_EXCEPTION",
            Meaning      = "That code is UNEXPECTED_STORE_EXCEPTION — the memory compression store hit an error it did not expect.",
            CommonCauses = "Often storage-related despite sounding like a memory problem. Also antivirus interference and driver bugs.",
            Checks = ["Check SMART health.", "Run chkdsk /f.", "Temporarily disable third-party antivirus and retest."],
        },
        [0x12B] = new()
        {
            Name         = "FAULTY_HARDWARE_CORRUPTED_PAGE",
            Meaning      = "That code is FAULTY_HARDWARE_CORRUPTED_PAGE — a single-bit error was found in memory, which Windows attributes to hardware.",
            CommonCauses = "Faulty RAM or an unstable memory overclock, more directly than most codes.",
            Checks = ["Test RAM with MemTest86.", "Set memory to stock speeds.", "Test sticks individually."],
        },
    };
}
