using System.Text.Json.Serialization;

namespace ExplainMyPC.Services.Startup;

/// <summary>
/// A point-in-time snapshot of the system's entire startup configuration.
///
/// Captured by <c>StartupStateBackupExecutor</c> and serialised to a JSON
/// file in <c>%LOCALAPPDATA%\ExplainMyPC\StartupBackups\</c>.
///
/// Used by <c>StartupStateRestoreExecutor</c> to revert the startup list
/// to its state at the time of the snapshot.
///
/// JSON is the format of choice because:
///   • Human-readable — users can inspect and understand what will be restored.
///   • Easy to share or include in bug reports.
///   • No binary format migration concerns.
/// </summary>
public sealed record StartupBackupSnapshot(
    [property: JsonPropertyName("createdAt")]   DateTimeOffset              CreatedAt,
    [property: JsonPropertyName("machineName")] string                      MachineName,
    [property: JsonPropertyName("entries")]     IReadOnlyList<StartupEntrySnapshot> Entries);

/// <summary>
/// The state of a single startup entry at snapshot time.
/// </summary>
public sealed record StartupEntrySnapshot(
    [property: JsonPropertyName("name")]       string         Name,
    [property: JsonPropertyName("command")]    string         Command,
    [property: JsonPropertyName("executable")] string         ExecutableName,
    [property: JsonPropertyName("location")]   StartupLocation Location,
    [property: JsonPropertyName("enabled")]    bool           IsEnabled,

    /// <summary>
    /// The raw hex representation of the StartupApproved registry value at
    /// snapshot time, or null if the entry was not present in that key.
    /// Stored so restore can put the key back byte-for-byte rather than
    /// using a synthetic enabled/disabled value.
    /// </summary>
    [property: JsonPropertyName("approvedRaw")] string? ApprovedRawValue = null);
