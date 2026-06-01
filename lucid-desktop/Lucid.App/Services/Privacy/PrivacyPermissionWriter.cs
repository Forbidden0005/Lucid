using Microsoft.Win32;

namespace Lucid.Services.Privacy;

/// <summary>
/// Writes per-app permission grants to the Windows CapabilityAccessManager ConsentStore.
///
/// Registry path written (current user):
///   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore
///      \{capabilityName}\{appIdentifier}\Value  (REG_SZ: "Allow" | "Deny")
///
/// Safety:
///   Only writes the "Value" key of an existing app subkey — never creates new registry
///   keys or modifies the system-wide capability toggle.
///   Returns false silently if the target key is absent or access is denied.
///
/// Threading:
///   All operations are synchronous. Wrap in Task.Run when calling from the UI thread.
/// </summary>
public static class PrivacyPermissionWriter
{
    private const string ConsentStorePath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    /// <summary>
    /// Sets the per-app permission value for a specific capability.
    ///
    /// Returns <c>true</c> when the write succeeded; <c>false</c> when the registry
    /// key was not found or the write was denied (read-only environment, Group Policy, etc.).
    /// </summary>
    /// <param name="capabilityName">
    /// Capability registry key name (e.g. <c>"webcam"</c>, <c>"microphone"</c>).
    /// </param>
    /// <param name="appIdentifier">
    /// App registry subkey name (e.g. <c>"Microsoft.Windows.Photos_8wekyb3d8bbwe"</c>
    /// or <c>"NonPackaged\C:#Windows#System32#svchost.exe"</c>).
    /// </param>
    /// <param name="allow">
    /// <c>true</c> to set the value to <c>"Allow"</c>;
    /// <c>false</c> to set it to <c>"Deny"</c>.
    /// </param>
    public static bool TrySetAppPermission(
        string capabilityName,
        string appIdentifier,
        bool   allow)
    {
        try
        {
            // Open the app-specific subkey with write access.
            // This key must already exist — we never create new subkeys.
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"{ConsentStorePath}\{capabilityName}\{appIdentifier}",
                writable: true);

            if (key is null) return false;

            key.SetValue("Value", allow ? "Allow" : "Deny", RegistryValueKind.String);
            return true;
        }
        catch
        {
            // Registry access denied, key locked, or Group Policy is overriding.
            // Fail silently — the caller will leave the UI in its previous state.
            return false;
        }
    }
}
