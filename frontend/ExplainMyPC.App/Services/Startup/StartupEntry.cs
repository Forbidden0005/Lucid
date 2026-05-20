namespace ExplainMyPC.Services.Startup;

/// <summary>
/// Where the startup entry is registered.
/// </summary>
public enum StartupLocation
{
    /// <summary>HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run (current user).</summary>
    HkcuRun,

    /// <summary>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run (all users, usually requires elevation to modify).</summary>
    HklmRun,

    /// <summary>%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup folder.</summary>
    StartupFolder,
}

/// <summary>
/// Estimated resource cost of keeping this app in the startup list.
///
/// Classification is heuristic — based on the known resource behaviour of
/// popular applications, not a live measurement. High-impact apps tend to
/// either hold background services, poll the network continuously, or load
/// large runtimes (e.g. Electron, Java) before the user opens them.
/// </summary>
public enum StartupImpact
{
    /// <summary>Impact not assessed — exe name not in the known-app catalogue.</summary>
    Unknown  = 0,

    /// <summary>Lightweight; typically a small tray icon or native daemon.</summary>
    Low      = 1,

    /// <summary>Moderate background activity; e.g. cloud sync clients.</summary>
    Moderate = 2,

    /// <summary>
    /// Resource-intensive; e.g. browsers, Electron communication apps,
    /// gaming launchers, or media players that install background services.
    /// </summary>
    High     = 3,
}

/// <summary>
/// A single application (or script) that Windows launches automatically at sign-in.
///
/// Produced by <see cref="StartupSampler"/>. Immutable — the sampler rebuilds
/// the list from scratch every refresh cycle rather than mutating entries.
/// </summary>
/// <param name="Name">Display name from the registry value name or folder shortcut name.</param>
/// <param name="Command">Full command string (path + arguments) as stored in the registry.</param>
/// <param name="ExecutableName">
///     Lower-case filename without extension, e.g. "chrome" or "discord".
///     Used by insight rules for impact lookup and display.
/// </param>
/// <param name="Location">Registry key or folder where the entry is registered.</param>
/// <param name="Impact">Estimated resource cost when this app runs at startup.</param>
/// <param name="IsEnabled">
///     True for entries found in the normal Run keys / Startup folder.
///     (Disabled entries stored under RunOnce-disabled keys are excluded for now.)
/// </param>
public sealed record StartupEntry(
    string         Name,
    string         Command,
    string         ExecutableName,
    StartupLocation Location,
    StartupImpact  Impact,
    bool           IsEnabled = true);
