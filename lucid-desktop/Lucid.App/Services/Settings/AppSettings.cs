namespace Lucid.Services.Settings;

/// <summary>
/// Immutable snapshot of all user-configurable settings.
///
/// Persisted to %LocalAppData%\Lucid\settings.json by <see cref="JsonSettingsStore"/>.
/// All fields carry defaults so a missing or partially-written file never causes
/// a crash — the app always boots with sensible values.
///
/// Schema versioning:
///   Increment <see cref="CurrentSchemaVersion"/> when a breaking structural change
///   is made. JsonSettingsStore runs the migration table before returning the record.
///   Additive fields (new properties with defaults) do NOT require a version bump.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Current schema version embedded in every written file.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version read from the file — compared against CurrentSchemaVersion on load.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    // ── Appearance ────────────────────────────────────────────────────────────

    /// <summary>True = dark theme (default); false = light theme.</summary>
    public bool DarkModeEnabled { get; init; } = true;

    // ── Telemetry & Scanning ──────────────────────────────────────────────────

    /// <summary>Enable periodic background scans (off by default).</summary>
    public bool AutoScanEnabled { get; init; } = false;

    /// <summary>Days between automatic background scans.</summary>
    public int AutoScanIntervalDays { get; init; } = 7;

    // ── Privacy ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Opt-in anonymous usage telemetry — always off by default.
    /// Per security-model: never auto-enabled; user must explicitly enable.
    /// </summary>
    public bool UsageTelemetryEnabled { get; init; } = false;

    // ── AI / LLM ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Base URL of the Ollama inference server.
    /// Default: <c>"http://localhost:11434"</c> (standard Ollama install).
    /// </summary>
    public string LlmEndpointUrl { get; init; } = "http://localhost:11434";

    /// <summary>
    /// Ollama model name to use for the AI companion.
    /// Must be already pulled in Ollama (e.g. <c>ollama pull llama3.2:3b</c>).
    /// Default: <c>"llama3.2:3b"</c>.
    /// </summary>
    public string LlmModel { get; init; } = "llama3.2:3b";

    // ── Operational Trust ─────────────────────────────────────────────────────

    /// <summary>
    /// Serialized <c>TrustConsentMode</c> enum name.
    /// Stored as a string for human-readability and resilience against renumbering.
    /// Default: <c>"AskForMediumAndHighRisk"</c>.
    /// </summary>
    public string ConsentMode { get; init; } = "AskForMediumAndHighRisk";

    /// <summary>
    /// Serialized <c>AutomationMode</c> enum name.
    /// Default: <c>"ConfirmBeforeAction"</c>.
    /// </summary>
    public string AutomationMode { get; init; } = "ConfirmBeforeAction";

    // ── Companion overlay ─────────────────────────────────────────────────────

    /// <summary>
    /// Last saved X position (screen pixels) of the companion overlay window.
    /// Null = use the default bottom-right position near the taskbar.
    /// Additive field — no schema version bump required.
    /// </summary>
    public int? CompanionPositionX { get; init; } = null;

    /// <summary>
    /// Last saved Y position (screen pixels) of the companion overlay window.
    /// Null = use the default bottom-right position near the taskbar.
    /// </summary>
    public int? CompanionPositionY { get; init; } = null;
}
