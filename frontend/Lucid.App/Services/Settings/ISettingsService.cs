namespace Lucid.Services.Settings;

/// <summary>
/// Persistent user settings service.
///
/// The service owns the single source of truth for all user preferences.
/// It loads settings from disk on construction (synchronous, fast) and
/// writes atomically on every <see cref="SaveAsync"/> call.
///
/// Design constraints:
///   • Thread-safe — concurrent reads are always safe; writes are serialised.
///   • Fault-tolerant — file corruption never crashes; defaults are used instead.
///   • Atomic — a partial write never leaves a corrupt settings file.
///   • Event-driven — subscribers can react to changes without polling.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Current settings snapshot.
    /// The returned record is immutable — apply modifications with <c>with</c> expressions
    /// and pass the result to <see cref="SaveAsync"/>.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>
    /// Atomically persists <paramref name="settings"/> to disk and updates
    /// <see cref="Current"/>.  Raises <see cref="SettingsChanged"/> after the
    /// write completes.
    /// </summary>
    Task SaveAsync(AppSettings settings);

    /// <summary>
    /// Raised on the calling thread after settings are successfully saved.
    /// The event argument is the new <see cref="AppSettings"/> snapshot.
    /// </summary>
    event EventHandler<AppSettings>? SettingsChanged;
}
