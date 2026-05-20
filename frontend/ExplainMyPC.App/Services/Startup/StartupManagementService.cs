using Microsoft.Win32;

namespace ExplainMyPC.Services.Startup;

/// <summary>
/// Concrete implementation of <see cref="IStartupManagementService"/>.
///
/// Uses the Windows StartupApproved registry mechanism — the same mechanism
/// Windows Task Manager and Settings → Apps → Startup use to enable/disable
/// startup items without removing them from the Run key.
///
/// StartupApproved registry layout:
///   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run
///   HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run
///   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder
///
/// Each value name matches the Run-key value name (or shortcut filename for the folder).
/// The binary data is 12 bytes:
///   [0..3]  — flag bytes: 0x02 0x00 0x00 0x00 = enabled
///                         0x03 0x00 0x00 0x00 = disabled
///   [4..11] — FILETIME (8 bytes, UTC) of when the state was set.
///             All zeros for enabled values created by this service.
///
/// Absence from the key is treated as enabled by Windows (and this service).
/// </summary>
public sealed class StartupManagementService : IStartupManagementService
{
    // ── Registry paths ────────────────────────────────────────────────────────

    private const string ApprovedRunHkcu =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ApprovedRunHklm =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ApprovedFolderHkcu =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    // Flag byte sequences (12 bytes total: 4-byte flag + 8-byte zero FILETIME)
    private static readonly byte[] s_enabledValue  = [0x02, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] s_disabledValue = [0x03, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0];

    // ── IStartupManagementService ─────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<StartupEntry> GetAllEntries()
    {
        var raw = new StartupSampler().Sample();
        var list = new List<StartupEntry>(raw.Count);

        foreach (var entry in raw)
        {
            bool disabled = IsDisabled(entry.Name, entry.Location);
            list.Add(entry with { IsEnabled = !disabled });
        }

        return list.AsReadOnly();
    }

    /// <inheritdoc/>
    public bool IsDisabled(string entryName, StartupLocation location)
    {
        try
        {
            var (hive, subKey) = GetApprovedKey(location);
            using var key = hive.OpenSubKey(subKey, writable: false);
            if (key is null) return false;

            var val = key.GetValue(entryName, null) as byte[];
            if (val is null || val.Length < 1) return false;

            // First byte 0x03 = disabled; 0x02 = enabled; anything else = enabled.
            return val[0] == 0x03;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public bool DisableEntry(string entryName, StartupLocation location)
    {
        try
        {
            var (hive, subKey) = GetApprovedKey(location);
            using var key = hive.CreateSubKey(subKey, writable: true);
            if (key is null) return false;

            // Write the current timestamp into bytes 4–11 so Windows knows when it was disabled.
            var value = (byte[])s_disabledValue.Clone();
            var ft    = DateTimeToFiletime(DateTimeOffset.UtcNow);
            Buffer.BlockCopy(BitConverter.GetBytes(ft), 0, value, 4, 8);

            key.SetValue(entryName, value, RegistryValueKind.Binary);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public bool EnableEntry(string entryName, StartupLocation location)
    {
        try
        {
            var (hive, subKey) = GetApprovedKey(location);
            using var key = hive.CreateSubKey(subKey, writable: true);
            if (key is null) return false;

            key.SetValue(entryName, s_enabledValue, RegistryValueKind.Binary);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public string? GetApprovedRawValue(string entryName, StartupLocation location)
    {
        try
        {
            var (hive, subKey) = GetApprovedKey(location);
            using var key = hive.OpenSubKey(subKey, writable: false);
            if (key is null) return null;

            var val = key.GetValue(entryName, null) as byte[];
            return val is null ? null : Convert.ToHexString(val);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public bool RestoreApprovedRawValue(
        string entryName, StartupLocation location, string? hexValue)
    {
        try
        {
            var (hive, subKey) = GetApprovedKey(location);

            if (string.IsNullOrEmpty(hexValue))
            {
                // Restore by deleting the value (implicit enabled state).
                using var key = hive.OpenSubKey(subKey, writable: true);
                key?.DeleteValue(entryName, throwOnMissingValue: false);
                return true;
            }

            using var writeKey = hive.CreateSubKey(subKey, writable: true);
            if (writeKey is null) return false;

            var bytes = Convert.FromHexString(hexValue);
            writeKey.SetValue(entryName, bytes, RegistryValueKind.Binary);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Registry key routing ──────────────────────────────────────────────────

    private static (RegistryKey Hive, string SubKey) GetApprovedKey(StartupLocation location) =>
        location switch
        {
            StartupLocation.HkcuRun      => (Registry.CurrentUser,   ApprovedRunHkcu),
            StartupLocation.HklmRun      => (Registry.LocalMachine,  ApprovedRunHklm),
            StartupLocation.StartupFolder => (Registry.CurrentUser,  ApprovedFolderHkcu),
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
        };

    // ── FILETIME helper ───────────────────────────────────────────────────────

    private static long DateTimeToFiletime(DateTimeOffset dt) =>
        dt.UtcDateTime.ToFileTimeUtc();
}
