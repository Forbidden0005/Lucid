namespace Lucid.Services.Privacy;

/// <summary>
/// Immutable snapshot of a single app's grant state for one privacy permission.
/// Produced by <see cref="PrivacyPermissionScanner"/>. Never mutated after creation.
/// </summary>
public sealed record PrivacyPermissionRecord
{
    /// <summary>Raw capability key name (e.g. "webcam", "microphone").</summary>
    public string          PermissionType { get; init; } = "";

    /// <summary>Human-readable app name extracted from the registry key.</summary>
    public string          AppDisplayName { get; init; } = "";

    /// <summary>Raw registry subkey name (package family name or NonPackaged path).</summary>
    public string          AppIdentifier  { get; init; } = "";

    /// <summary>True when the app's access is allowed; false when denied.</summary>
    public bool            IsAllowed      { get; init; }

    /// <summary>True for UWP / MSIX packaged apps; false for Win32 desktop apps.</summary>
    public bool            IsPackaged     { get; init; }

    /// <summary>
    /// When this app last used the permission, derived from the LastUsedTimeStop
    /// FILETIME registry value. Null when the value is absent or zero (never used).
    /// </summary>
    public DateTimeOffset? LastUsed       { get; init; }
}

/// <summary>
/// Aggregated snapshot for one privacy permission category (e.g. Camera, Microphone).
/// Contains the global on/off state plus per-app grant records.
/// </summary>
public sealed record PrivacyCategoryRecord
{
    /// <summary>Raw capability key name (e.g. "webcam").</summary>
    public string                                PermissionType  { get; init; } = "";

    /// <summary>Display-friendly name (e.g. "Camera").</summary>
    public string                                DisplayName     { get; init; } = "";

    /// <summary>Segoe MDL2 Assets glyph character for this permission type.</summary>
    public string                                Glyph           { get; init; } = "";

    /// <summary>
    /// Whether this permission is globally enabled at the system level.
    /// When false, all app grants are effectively blocked regardless of per-app state.
    /// </summary>
    public bool                                  GloballyEnabled { get; init; }

    /// <summary>All apps that have requested this permission, sorted allowed-first.</summary>
    public IReadOnlyList<PrivacyPermissionRecord> Apps           { get; init; } = [];
}
