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
///   Only writes the "Value" key of an existing app subkey and never creates new registry
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
        bool allow) =>
        TrySetAppPermission(
            Registry.CurrentUser,
            ConsentStorePath,
            capabilityName,
            appIdentifier,
            allow);

    internal static bool TrySetAppPermission(
        RegistryKey registryRoot,
        string consentStorePath,
        string capabilityName,
        string appIdentifier,
        bool allow)
    {
        if (registryRoot is null)
            throw new ArgumentNullException(nameof(registryRoot));

        if (string.IsNullOrWhiteSpace(consentStorePath))
            throw new ArgumentException("Consent store path must not be empty.", nameof(consentStorePath));

        if (!IsValidCapabilityName(capabilityName) ||
            string.IsNullOrWhiteSpace(appIdentifier))
            return false;

        try
        {
            // Open the app-specific subkey with write access.
            // This key must already exist and we never create new subkeys.
            using var key = registryRoot.OpenSubKey(
                $@"{consentStorePath}\{capabilityName}\{appIdentifier}",
                writable: true);

            if (key is null)
                return false;

            key.SetValue("Value", allow ? "Allow" : "Deny", RegistryValueKind.String);
            return true;
        }
        catch
        {
            // Registry access denied, key locked, or Group Policy is overriding.
            // Fail silently so the caller can keep the previous UI state.
            return false;
        }
    }

    internal static bool IsValidCapabilityName(string? capabilityName)
    {
        if (string.IsNullOrWhiteSpace(capabilityName))
            return false;

        return capabilityName.IndexOfAny(['\\', '/', ':']) < 0;
    }
}
