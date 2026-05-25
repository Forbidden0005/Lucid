using System.Diagnostics;

namespace Lucid.Services.Automation;

/// <summary>
/// Opens system tools and Windows settings pages on the user's behalf.
///
/// Every method here is zero-risk: it only launches a window/page — nothing
/// on the system is changed. These are the equivalent of the user clicking
/// an icon themselves.
///
/// Supported targets:
///   • Task Manager (CPU/memory/process view)
///   • Device Manager (driver and device status)
///   • Windows Settings pages (storage, startup, network, apps, etc.)
///   • Event Viewer
///   • Network troubleshooter
///   • Windows Security
///   • Disk Cleanup
///   • Resource Monitor
///
/// All methods return (bool success, string? errorMessage).
/// </summary>
public sealed class ApplicationLaunchAutomation
{
    // ── Task Manager ──────────────────────────────────────────────────────────

    /// <summary>Opens Task Manager. Safe, zero-risk.</summary>
    public async Task<(bool Success, string? Error)> OpenTaskManagerAsync()
        => await LaunchProcessAsync("taskmgr.exe");

    // ── System management tools ───────────────────────────────────────────────

    /// <summary>Opens Device Manager.</summary>
    public async Task<(bool Success, string? Error)> OpenDeviceManagerAsync()
        => await LaunchProcessAsync("devmgmt.msc");

    /// <summary>Opens the Windows Event Viewer.</summary>
    public async Task<(bool Success, string? Error)> OpenEventViewerAsync()
        => await LaunchProcessAsync("eventvwr.msc");

    /// <summary>Opens Resource Monitor (resmon.exe — deeper than Task Manager).</summary>
    public async Task<(bool Success, string? Error)> OpenResourceMonitorAsync()
        => await LaunchProcessAsync("resmon.exe");

    /// <summary>Opens Disk Cleanup for the C: drive.</summary>
    public async Task<(bool Success, string? Error)> OpenDiskCleanupAsync()
        => await LaunchProcessAsync("cleanmgr.exe", "/d C:");

    /// <summary>Opens the System Information tool (msinfo32).</summary>
    public async Task<(bool Success, string? Error)> OpenSystemInfoAsync()
        => await LaunchProcessAsync("msinfo32.exe");

    // ── Windows Settings pages (ms-settings: URI scheme) ─────────────────────

    /// <summary>Opens Windows Security (Defender).</summary>
    public async Task<(bool Success, string? Error)> OpenWindowsSecurityAsync()
        => await LaunchShellAsync("windowsdefender:");

    /// <summary>Opens the Startup Apps page in Task Manager.</summary>
    public async Task<(bool Success, string? Error)> OpenStartupAppsAsync()
        => await LaunchShellAsync("ms-settings:startupapps");

    /// <summary>Opens the Storage settings page.</summary>
    public async Task<(bool Success, string? Error)> OpenStorageSettingsAsync()
        => await LaunchShellAsync("ms-settings:storagesense");

    /// <summary>Opens the Apps &amp; Features settings page.</summary>
    public async Task<(bool Success, string? Error)> OpenAppsSettingsAsync()
        => await LaunchShellAsync("ms-settings:appsfeatures");

    /// <summary>Opens the Network settings page.</summary>
    public async Task<(bool Success, string? Error)> OpenNetworkSettingsAsync()
        => await LaunchShellAsync("ms-settings:network");

    /// <summary>Opens the Windows Update settings page.</summary>
    public async Task<(bool Success, string? Error)> OpenWindowsUpdateAsync()
        => await LaunchShellAsync("ms-settings:windowsupdate");

    /// <summary>Opens the Privacy &amp; Security settings page.</summary>
    public async Task<(bool Success, string? Error)> OpenPrivacySettingsAsync()
        => await LaunchShellAsync("ms-settings:privacy");

    /// <summary>Opens the Power &amp; Battery settings page.</summary>
    public async Task<(bool Success, string? Error)> OpenPowerSettingsAsync()
        => await LaunchShellAsync("ms-settings:powersleep");

    /// <summary>Opens the About / System Information settings page.</summary>
    public async Task<(bool Success, string? Error)> OpenAboutSettingsAsync()
        => await LaunchShellAsync("ms-settings:about");

    // ── Troubleshooters ───────────────────────────────────────────────────────

    /// <summary>Opens the Windows Network troubleshooter.</summary>
    public async Task<(bool Success, string? Error)> OpenNetworkTroubleshooterAsync()
        => await LaunchShellAsync("ms-settings:troubleshoot");

    /// <summary>Opens the Windows Update troubleshooter.</summary>
    public async Task<(bool Success, string? Error)> OpenUpdateTroubleshooterAsync()
        => await LaunchShellAsync("ms-settings:troubleshoot");

    // ── Generic launcher (by ms-settings: URI) ────────────────────────────────

    /// <summary>
    /// Opens any Windows Settings page by URI.
    /// Example: OpenSettingsPageAsync("ms-settings:display")
    /// </summary>
    public async Task<(bool Success, string? Error)> OpenSettingsPageAsync(string settingsUri)
        => await LaunchShellAsync(settingsUri);

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<(bool Success, string? Error)> LaunchProcessAsync(
        string executable, string? args = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName        = executable,
                UseShellExecute = true,
            };
            if (!string.IsNullOrEmpty(args))
                psi.Arguments = args;

            Process.Start(psi);
            await Task.Delay(200);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Could not open {executable}: {ex.Message}");
        }
    }

    private static async Task<(bool Success, string? Error)> LaunchShellAsync(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = uri,
                UseShellExecute = true,
            });
            await Task.Delay(200);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Could not open {uri}: {ex.Message}");
        }
    }
}
