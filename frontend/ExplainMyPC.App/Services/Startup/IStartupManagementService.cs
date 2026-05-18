namespace ExplainMyPC.Services.Startup;

/// <summary>
/// Write-side contract for startup entry management.
///
/// Extends the read-only <see cref="StartupSampler"/> with the ability to
/// disable and enable startup entries by writing to the
/// <c>StartupApproved</c> registry keys that Windows Task Manager and
/// Windows Settings use to track disabled entries.
///
/// Design:
///   • Read-only operations (GetAllEntries, IsDisabled) are synchronous and
///     non-destructive — safe to call from any thread.
///   • Write operations (DisableEntry, EnableEntry) are also synchronous and
///     lightweight (single registry write).  Callers are responsible for
///     marshalling off the UI thread.
///   • All methods return a bool success flag — never throw.
///     Callers should log failures from context.Log.
/// </summary>
public interface IStartupManagementService
{
    /// <summary>
    /// Returns all startup entries visible to the current user, including
    /// disabled ones.  IsEnabled is set correctly by cross-referencing the
    /// StartupApproved registry keys.
    /// </summary>
    IReadOnlyList<StartupEntry> GetAllEntries();

    /// <summary>
    /// Returns <see langword="true"/> when the named entry has been disabled
    /// via the StartupApproved mechanism.
    /// </summary>
    bool IsDisabled(string entryName, StartupLocation location);

    /// <summary>
    /// Disables the startup entry identified by <paramref name="entryName"/>
    /// at the given <paramref name="location"/> by writing a "disabled" value
    /// to the corresponding StartupApproved sub-key.
    ///
    /// HKLM entries require the process to be elevated — the caller should
    /// check privilege before calling this for HKLM locations.
    ///
    /// Returns <see langword="true"/> on success, <see langword="false"/> on
    /// registry access failure.
    /// </summary>
    bool DisableEntry(string entryName, StartupLocation location);

    /// <summary>
    /// Re-enables a previously disabled startup entry by writing an "enabled"
    /// value to the corresponding StartupApproved sub-key.
    ///
    /// Returns <see langword="true"/> on success.
    /// </summary>
    bool EnableEntry(string entryName, StartupLocation location);

    /// <summary>
    /// Reads the raw binary value currently stored in the StartupApproved
    /// key for the named entry, as a hex string.
    ///
    /// Returns <see langword="null"/> when no value exists (i.e. the entry
    /// has never been touched by the approval mechanism — treat as enabled).
    ///
    /// Used by the rollback system to restore the exact previous state.
    /// </summary>
    string? GetApprovedRawValue(string entryName, StartupLocation location);

    /// <summary>
    /// Writes a raw binary value back to the StartupApproved key for the
    /// named entry (used during rollback to restore the previous state).
    ///
    /// If <paramref name="hexValue"/> is <see langword="null"/> or empty,
    /// the value is deleted (restoring the implicit "enabled" state).
    ///
    /// Returns <see langword="true"/> on success.
    /// </summary>
    bool RestoreApprovedRawValue(
        string entryName, StartupLocation location, string? hexValue);
}
