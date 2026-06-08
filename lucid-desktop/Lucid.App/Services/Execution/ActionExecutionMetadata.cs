namespace Lucid.Services.Execution;

/// <summary>
/// Coarse operational class for an executor. Used for governance, diagnostics,
/// and UI explanation surfaces that need a stable action grouping.
/// </summary>
public enum ActionExecutionResourceClass
{
    Navigation = 0,
    Cleanup = 1,
    StartupManagement = 2,
    ProcessControl = 3,
    Repair = 4,
    SecurityReview = 5,
}

/// <summary>
/// Explicit production contract metadata for one executor action.
/// This complements the executor interface with reader-facing and diagnostics-
/// facing guarantees that should not live only in comments.
/// </summary>
public sealed record ActionExecutionMetadata(
    string ActionId,
    ActionExecutionResourceClass ResourceClass,
    ActionPrivilegeLevel RequiredPrivilege,
    bool RequiresConfirmation,
    bool SupportsDryRun,
    bool SupportsRollback,
    bool IsReversible,
    string ConsentNote,
    string FailureModeSummary,
    string DiagnosticsCategory);

/// <summary>
/// Canonical metadata catalog for all registered executors.
/// Registration validates against this catalog so metadata drift fails fast.
/// </summary>
public static class ActionExecutionMetadataCatalog
{
    private static readonly Dictionary<string, ActionExecutionMetadata> s_entries =
        new(StringComparer.OrdinalIgnoreCase);

    static ActionExecutionMetadataCatalog()
    {
        Register(new("action.disk.clean-browser-cache", ActionExecutionResourceClass.Cleanup,
            ActionPrivilegeLevel.Standard, false, true, false, false,
            "Browser cache cleanup does not require confirmation because it only removes temporary cache files.",
            "Open browser files may be skipped and some browsers may rebuild cache on next launch.",
            "Execution.Cleanup"));
        Register(new("action.storage.clean-old-downloads", ActionExecutionResourceClass.Cleanup,
            ActionPrivilegeLevel.Standard, true, true, true, true,
            "Old downloads can still be user data, so review the scope before Lucid stages them for deletion.",
            "Missing or locked files may be skipped and only staged files can be restored.",
            "Execution.Cleanup"));
        Register(new("action.storage.delete-duplicate-group", ActionExecutionResourceClass.Cleanup,
            ActionPrivilegeLevel.Standard, true, true, true, true,
            "Duplicate cleanup can remove user files, so Lucid requires confirmation before staging them.",
            "Locked, missing, or recently changed files may be skipped and only staged files can be restored.",
            "Execution.Cleanup"));
        Register(new("action.storage.delete-large-file", ActionExecutionResourceClass.Cleanup,
            ActionPrivilegeLevel.Standard, true, true, true, true,
            "Large-file deletion can remove user data, so Lucid requires confirmation before staging it.",
            "Missing, locked, or conflicting destination files can prevent deletion or later restoration.",
            "Execution.Cleanup"));
        Register(new("action.disk.clean-delivery-optimization", ActionExecutionResourceClass.Cleanup,
            ActionPrivilegeLevel.Administrator, true, true, true, true,
            "Delivery Optimization cleanup removes cached update data and requires confirmation because it touches protected Windows cache paths.",
            "Service stop failures or locked files may lead to partial cleanup, but staged files remain restorable.",
            "Execution.Cleanup"));
        Register(new("action.process.open-location", ActionExecutionResourceClass.Navigation,
            ActionPrivilegeLevel.Standard, false, true, false, false,
            "Opening a process location only launches Explorer and does not modify system state.",
            "Explorer may fail to open if the path no longer exists or access is denied.",
            "Execution.Process"));
        Register(new("action.startup.open-startup-apps", ActionExecutionResourceClass.Navigation,
            ActionPrivilegeLevel.Standard, false, true, false, false,
            "Opening Startup Apps only navigates to a settings page and does not modify startup state.",
            "Windows may not open the settings page if the URI handler is unavailable.",
            "Execution.Navigation"));
        Register(new("action.disk.run-storage-sense", ActionExecutionResourceClass.Navigation,
            ActionPrivilegeLevel.Standard, false, true, false, false,
            "Opening Storage Sense only navigates to a Windows settings page and does not modify files directly.",
            "Windows may not open the settings page if the URI handler is unavailable.",
            "Execution.Navigation"));
        Register(new("action.cpu.open-task-manager", ActionExecutionResourceClass.Navigation,
            ActionPrivilegeLevel.Standard, false, true, false, false,
            "Opening Task Manager is a navigation action with no system changes.",
            "Task Manager may fail to launch if shell execution is unavailable.",
            "Execution.Navigation"));
        Register(new("action.gpu.inspect-processes", ActionExecutionResourceClass.Navigation,
            ActionPrivilegeLevel.Standard, false, true, false, false,
            "Opening Task Manager for GPU inspection is a navigation action with no system changes.",
            "Task Manager may fail to launch if shell execution is unavailable.",
            "Execution.Navigation"));
        Register(new("action.cpu.open-task-scheduler", ActionExecutionResourceClass.Navigation,
            ActionPrivilegeLevel.Standard, false, true, false, false,
            "Opening Task Scheduler is a navigation action with no system changes.",
            "Task Scheduler may fail to launch if shell execution is unavailable.",
            "Execution.Navigation"));
        Register(new("action.security.open-virustotal", ActionExecutionResourceClass.SecurityReview,
            ActionPrivilegeLevel.Standard, false, true, false, false,
            "Opening a VirusTotal search does not upload anything by itself and keeps the user in control of any later submission.",
            "Browser launch may fail or the destination site may be unavailable.",
            "Execution.Security"));
        Register(new("action.security.open-windows-security", ActionExecutionResourceClass.Navigation,
            ActionPrivilegeLevel.Standard, false, true, false, false,
            "Opening Windows Security is a navigation action with no direct system changes.",
            "Windows Security may fail to launch if the URI handler is unavailable.",
            "Execution.Navigation"));
        Register(new("action.disk.empty-recycle-bin", ActionExecutionResourceClass.Cleanup,
            ActionPrivilegeLevel.Standard, true, true, false, false,
            "Emptying the Recycle Bin permanently removes already-deleted items for the current user and cannot be undone by Lucid.",
            "Shell query or empty operations may fail, and once emptied the bin contents are not recoverable through this tool.",
            "Execution.Cleanup"));
        Register(new("action.startup.disable-startup-app", ActionExecutionResourceClass.StartupManagement,
            ActionPrivilegeLevel.Standard, false, true, true, true,
            "Disabling a startup app changes how the app launches at sign-in, but Lucid captures the prior state for restoration.",
            "Registry or StartupApproved access can fail, and elevation may still be needed for some machine-wide entries.",
            "Execution.Startup"));
        Register(new("action.startup.enable-startup-app", ActionExecutionResourceClass.StartupManagement,
            ActionPrivilegeLevel.Standard, false, true, true, true,
            "Enabling a startup app changes how the app launches at sign-in, but Lucid captures the prior state for restoration.",
            "Registry or StartupApproved access can fail, and some machine-wide entries may still require elevation.",
            "Execution.Startup"));
        Register(new("action.startup.backup-startup-state", ActionExecutionResourceClass.StartupManagement,
            ActionPrivilegeLevel.Standard, false, true, true, true,
            "Backing up startup state only writes a snapshot file and can be undone by deleting that snapshot.",
            "Snapshot creation may fail if the destination path is unavailable or unwritable.",
            "Execution.Startup"));
        Register(new("action.startup.restore-startup-state", ActionExecutionResourceClass.StartupManagement,
            ActionPrivilegeLevel.Standard, true, true, true, true,
            "Restoring a startup snapshot rewrites startup configuration, so Lucid requires confirmation and captures the pre-restore state for rollback.",
            "Snapshot parsing, registry writes, or missing entries can lead to partial restore results.",
            "Execution.Startup"));
        Register(new("action.disk.clean-temp-files", ActionExecutionResourceClass.Cleanup,
            ActionPrivilegeLevel.Standard, false, true, true, true,
            "Temp cleanup targets known temporary paths and stages removed files so the run can be reversed.",
            "Open or inaccessible files may be skipped, and cancellation triggers an automatic restore of moved files.",
            "Execution.Cleanup"));
        Register(new("action.process.terminate", ActionExecutionResourceClass.ProcessControl,
            ActionPrivilegeLevel.Standard, true, true, false, false,
            "Terminating a process can interrupt active work and cannot be undone by Lucid once the process exits.",
            "Access denial, process exit races, or child-process side effects can leave the target unchanged or partially interrupted.",
            "Execution.Process"));
        Register(new("action.disk.clean-windows-update-cache", ActionExecutionResourceClass.Cleanup,
            ActionPrivilegeLevel.Administrator, true, true, true, true,
            "Windows Update cache cleanup stops a Windows service and stages cache files, so Lucid requires confirmation before proceeding.",
            "Service stop failures or locked files can yield partial cleanup, but staged files remain restorable.",
            "Execution.Cleanup"));
        Register(new("action.repair.dism-restore-health", ActionExecutionResourceClass.Repair,
            ActionPrivilegeLevel.Administrator, false, true, false, false,
            "DISM repairs the component store in place and does not offer a meaningful rollback path.",
            "The command can fail due to servicing corruption, missing repair sources, or Windows Update access issues.",
            "Execution.Repair"));
        Register(new("action.network.flush-dns", ActionExecutionResourceClass.Repair,
            ActionPrivilegeLevel.Standard, false, true, false, true,
            "Flushing DNS clears cached resolver state only and Windows will naturally rebuild the cache as needed.",
            "The command may fail if ipconfig is unavailable or DNS client state is not accessible.",
            "Execution.Repair"));
        Register(new("action.network.reset-adapter", ActionExecutionResourceClass.Repair,
            ActionPrivilegeLevel.Administrator, true, true, false, false,
            "Resetting the network stack can remove custom TCP/IP configuration, so Lucid requires confirmation before applying defaults.",
            "Custom IP, DNS, VPN, or routing settings may need manual reconfiguration after the reset.",
            "Execution.Repair"));
        Register(new("action.repair.sfc-scannow", ActionExecutionResourceClass.Repair,
            ActionPrivilegeLevel.Administrator, false, true, false, false,
            "SFC repairs protected Windows files in place and does not expose a meaningful rollback path through Lucid.",
            "The scan can fail, find unrepairable files, or require follow-up DISM repair before system files can be corrected.",
            "Execution.Repair"));
        Register(new("action.apps.reset-windows-store", ActionExecutionResourceClass.Repair,
            ActionPrivilegeLevel.Standard, false, true, false, true,
            "Resetting the Windows Store cache clears temporary Store state only and the cache rebuilds automatically.",
            "The Store reset command may fail or the Store may need to rebuild cache state on next launch.",
            "Execution.Repair"));
        Register(new("action.network.winsock-reset", ActionExecutionResourceClass.Repair,
            ActionPrivilegeLevel.Administrator, true, true, false, false,
            "Resetting Winsock removes third-party catalog entries and requires confirmation because those changes cannot be rolled back by Lucid.",
            "Networking components that depend on custom Winsock providers may need reinstallation after the reset.",
            "Execution.Repair"));
    }

    private static void Register(ActionExecutionMetadata metadata) =>
        s_entries[metadata.ActionId] = metadata;

    public static ActionExecutionMetadata GetRequired(string actionId) =>
        s_entries.TryGetValue(actionId, out var metadata)
            ? metadata
            : throw new InvalidOperationException(
                $"Missing execution metadata for action '{actionId}'.");

    public static bool TryGet(string actionId, out ActionExecutionMetadata? metadata) =>
        s_entries.TryGetValue(actionId, out metadata);

    public static IReadOnlyCollection<ActionExecutionMetadata> All =>
        s_entries.Values.ToList().AsReadOnly();
}
