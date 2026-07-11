using Lucid.Services.Startup;

namespace Lucid.Services.Apps;

/// <summary>
/// Immutable snapshot of a single installed application as read from the
/// Windows Uninstall registry keys.
///
/// Produced by <see cref="InstalledAppScanner"/>. Never mutated after creation.
/// The <see cref="LinkedStartupEntry"/> field is populated when the app is also
/// registered to launch at sign-in (joined via <see cref="InstalledAppScanner"/>).
/// </summary>
public sealed record InstalledAppRecord
{
    /// <summary>Display name (from the DisplayName registry value).</summary>
    public string Name { get; init; } = "";

    /// <summary>Publisher / company name (from the Publisher registry value). Empty when absent.</summary>
    public string Publisher { get; init; } = "";

    /// <summary>Version string (from the DisplayVersion registry value). Empty when absent.</summary>
    public string Version { get; init; } = "";

    /// <summary>Install date parsed from the YYYYMMDD registry value. Null when unavailable.</summary>
    public DateOnly? InstallDate { get; init; }

    /// <summary>
    /// Estimated installation size in kilobytes (from the EstimatedSize registry value).
    /// Zero when the value is absent or unreadable.
    /// </summary>
    public long EstimatedSizeKb { get; init; }

    /// <summary>
    /// Startup entry linked to this application, when the app is registered to run at sign-in.
    /// Null when the app has no startup registration.
    /// </summary>
    public StartupEntry? LinkedStartupEntry { get; init; }

    /// <summary>
    /// The app's registered interactive uninstall command (the UninstallString
    /// registry value — the same command Windows Settings launches). Empty when
    /// the app does not register one. Deliberately NOT the quiet/silent variant:
    /// launching this shows the app's own uninstaller UI, so removal always
    /// happens with the user's explicit confirmation in that flow.
    /// </summary>
    public string UninstallCommand { get; init; } = "";
}
