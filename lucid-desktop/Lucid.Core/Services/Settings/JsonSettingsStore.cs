using System.Text.Json;

namespace Lucid.Services.Settings;

/// <summary>
/// JSON-backed implementation of <see cref="ISettingsService"/>.
///
/// Persistence:
///   %LocalAppData%\Lucid\settings.json
///
/// Write strategy:
///   1. Serialise to JSON (indented, human-readable).
///   2. Write to a sibling .tmp file (atomic within the same directory/volume).
///   3. Rename .tmp → settings.json with overwrite — the OS guarantees this is
///      atomic on both NTFS and exFAT, so the live file is never corrupt.
///
/// Schema migration:
///   When <see cref="AppSettings.SchemaVersion"/> in the file is less than
///   <see cref="AppSettings.CurrentSchemaVersion"/>, the loaded record is
///   passed through the migration table.  Additive changes (new properties
///   with defaults) never require a migration entry — System.Text.Json ignores
///   missing JSON keys and fills in the record defaults automatically.
///
/// Failure modes:
///   • File not found → AppSettings defaults (silent, expected on first run).
///   • JSON parse error → AppSettings defaults (silent; corrupt file left intact).
///   • Write failure → exception propagated to caller via SaveAsync.
/// </summary>
public sealed class JsonSettingsStore : ISettingsService
{
    // ── Paths ─────────────────────────────────────────────────────────────────

    private static readonly string DataDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lucid");

    private static readonly string SettingsFilePath =
        Path.Combine(DataDirectory, "settings.json");

    // ── Serializer options ────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
    };

    // ── State ─────────────────────────────────────────────────────────────────

    // volatile so unsynchronised reads always observe the most recent SaveAsync write.
    private volatile AppSettings _current;

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // ── ISettingsService ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public AppSettings Current => _current;

    /// <inheritdoc/>
    public event EventHandler<AppSettings>? SettingsChanged;

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Loads settings from disk synchronously.
    /// Construction is always safe — defaults are returned if the file is
    /// missing, empty, or unreadable.
    /// </summary>
    public JsonSettingsStore()
    {
        _current = LoadFromDisk();
    }

    // ── ISettingsService.SaveAsync ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task SaveAsync(AppSettings settings)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DataDirectory);

            var tempPath = SettingsFilePath + ".tmp";
            var json     = JsonSerializer.Serialize(settings, JsonOptions);

            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);

            // Atomic rename — never leaves a partial file in the live path.
            File.Move(tempPath, SettingsFilePath, overwrite: true);

            _current = settings;
        }
        finally
        {
            _writeLock.Release();
        }

        // Fire outside the lock so subscribers can call SaveAsync without deadlocking.
        SettingsChanged?.Invoke(this, settings);
    }

    // ── Disk load ─────────────────────────────────────────────────────────────

    private static AppSettings LoadFromDisk()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new AppSettings();   // First run — all defaults.

            var json = File.ReadAllText(SettingsFilePath);

            if (string.IsNullOrWhiteSpace(json))
                return new AppSettings();   // Empty file — use defaults.

            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (loaded is null)
                return new AppSettings();   // Null deserialise result — use defaults.

            // Run schema migration if the on-disk version is older.
            return loaded.SchemaVersion < AppSettings.CurrentSchemaVersion
                ? Migrate(loaded)
                : loaded;
        }
        catch
        {
            // Corrupt JSON, locked file, permission error — fall back to defaults.
            // The corrupt file is intentionally left untouched so the user can
            // inspect it.  It will be overwritten the next time SaveAsync is called.
            return new AppSettings();
        }
    }

    // ── Schema migration ───────────────────────────────────────────────────────

    /// <summary>
    /// Upgrades an older settings record to the current schema version.
    ///
    /// Migration rules:
    ///   v1 → current (no-op — v1 IS the current version).
    ///   Future versions: add cases for each SchemaVersion → SchemaVersion+1 step.
    /// </summary>
    private static AppSettings Migrate(AppSettings old)
    {
        // The starting version for migration is old.SchemaVersion.
        // Step through each version in order.
        // No migrations needed yet — v1 is the first version.
        // When a v2 is introduced, add:
        //   if (version == 1) { settings = settings with { NewField = defaultValue }; version = 2; }

        // Always return with the current schema version so the file is upgraded on next save.
        return old with { SchemaVersion = AppSettings.CurrentSchemaVersion };
    }
}
