using System.Runtime.InteropServices;

namespace Lucid.Services.VisualContext;

// ─────────────────────────────────────────────────────────────────────────────
// Enumerations
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Controls which level of visual analysis is permitted.
/// All modes except Disabled require an explicit consent prompt.
/// </summary>
public enum VisualAnalysisMode
{
    /// <summary>Visual analysis is turned off — no capture, no UIA walking.</summary>
    Disabled          = 0,

    /// <summary>
    /// UIA/window-title analysis only — no pixel capture.
    /// Low-friction: does not require the full consent card.
    /// </summary>
    GuideOnly         = 1,

    /// <summary>
    /// Analyzes the active foreground window via UIA tree + optional pixel capture.
    /// Requires explicit user consent.
    /// </summary>
    AnalyzeCurrentWindow = 2,

    /// <summary>
    /// Full desktop snapshot for spatial context.
    /// Requires the highest-priority consent card.
    /// </summary>
    AnalyzeVisibleDesktop = 3,
}

/// <summary>Semantic window type — covers the operational UI windows Lucid cares about.</summary>
public enum SemanticWindowType
{
    Unknown             = 0,

    // ── System tools ─────────────────────────────────────────────────────────
    FileExplorer        = 1,
    WindowsSettings     = 2,
    DeviceManager       = 3,
    EventViewer         = 4,
    TaskManager         = 5,
    ControlPanel        = 6,
    ServicesManager     = 7,
    RegistryEditor      = 8,
    DiskManagement      = 9,
    SystemInformation   = 10,
    ResourceMonitor     = 11,
    PerformanceMonitor  = 12,

    // ── Settings subsections (detected from window title) ────────────────────
    StartupSettings     = 20,
    StorageSettings     = 21,
    PrivacySettings     = 22,
    NetworkSettings     = 23,
    DisplaySettings     = 24,
    WindowsUpdate       = 25,
    BluetoothSettings   = 26,
    AppsSettings        = 27,
    SecuritySettings    = 28,
    SoundSettings       = 29,
    AccessibilitySettings = 30,

    // ── Browsers ─────────────────────────────────────────────────────────────
    Browser             = 40,

    // ── Productivity ─────────────────────────────────────────────────────────
    MicrosoftStore      = 50,
    Terminal            = 51,
    TextEditor          = 52,

    // ── Dialogs ──────────────────────────────────────────────────────────────
    SystemDialog        = 60,
    OpenFileDialog      = 61,
    SaveFileDialog      = 62,
    PrintDialog         = 63,

    // ── Lucid itself ─────────────────────────────────────────────────────────
    LucidApp            = 70,
}

/// <summary>Sub-type for File Explorer windows.</summary>
public enum ExplorerViewType
{
    Unknown          = 0,
    Downloads        = 1,
    Documents        = 2,
    Desktop          = 3,
    Screenshots      = 4,
    Pictures         = 5,
    Videos           = 6,
    Music            = 7,
    Temp             = 8,
    ProgramFiles     = 9,
    UserProfile      = 10,
    GenericFolder    = 11,
    DriveRoot        = 12,
    RecycleBin       = 13,
    NetworkLocation  = 14,
    ProjectWorkspace = 15,
    OneDrive         = 16,
}

/// <summary>Specific Windows Settings page.</summary>
public enum SettingsPageType
{
    Unknown              = 0,
    MainSettings         = 1,
    StartupApps          = 2,
    StorageSense         = 3,
    PrivacySecurity      = 4,
    Network              = 5,
    Display              = 6,
    WindowsUpdate        = 7,
    Bluetooth            = 8,
    AppsFeatures         = 9,
    WindowsSecurity      = 10,
    Sound                = 11,
    Accessibility        = 12,
    System               = 13,
    Personalization      = 14,
    Accounts             = 15,
    TimeLanguage         = 16,
    Gaming               = 17,
    Troubleshoot         = 18,
    Recovery             = 19,
    DeviceManager        = 20,
}

/// <summary>
/// Screen-coordinate rectangle.  Uses int (physical pixels) to match Win32 conventions.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ScreenRect(int x, int y, int width, int height)
{
    public int X      { get; } = x;
    public int Y      { get; } = y;
    public int Width  { get; } = width;
    public int Height { get; } = height;

    public int Right  => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static readonly ScreenRect Empty = new(0, 0, 0, 0);

    public override string ToString() => $"({X},{Y}) {Width}×{Height}";
}

// ─────────────────────────────────────────────────────────────────────────────
// Core result types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single UI control detected inside a window via child-window enumeration.
/// BoundingRect is in screen coordinates (physical pixels).
/// </summary>
public sealed record DetectedControl
{
    public required string    Name        { get; init; }
    public required string    ControlType { get; init; }
    public          string?   Text        { get; init; }
    public          bool      IsEnabled   { get; init; } = true;
    public          ScreenRect BoundingRect { get; init; }
    public          string?   ClassName   { get; init; }
}

/// <summary>
/// A locatable operational target — a UI element the user can interact with.
/// Used by VisualWorkflowLocator to surface "where to click" guidance.
/// </summary>
public sealed record VisualWorkflowTarget
{
    public required string     Label        { get; init; }
    public required string     Description  { get; init; }
    public required ScreenRect ScreenRect   { get; init; }
    public          string?    ActionHint   { get; init; }
    public          double     Confidence   { get; init; }
    public          string?    WindowTitle  { get; init; }
}

/// <summary>
/// Fully interpreted semantic context of the active window.
/// Produced by WindowSemanticAnalyzer from P/Invoke window attributes.
/// </summary>
public sealed record SemanticWindowContext
{
    public required string              WindowTitle        { get; init; }
    public required string              ProcessName        { get; init; }
    public required string              ClassName          { get; init; }
    public required SemanticWindowType  WindowType         { get; init; }
    public required double              Confidence         { get; init; }
    public required ScreenRect          WindowRect         { get; init; }
    public          IntPtr              Hwnd               { get; init; }
    public          string?             ContextDescription { get; init; }
    public IReadOnlyList<string>        WorkflowHints      { get; init; } = [];
    public IReadOnlyList<string>        SuggestedActions   { get; init; } = [];
    public IReadOnlyList<DetectedControl> DetectedControls { get; init; } = [];
    public          DateTimeOffset      AnalyzedAt         { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// File Explorer window interpretation: what folder type and what observations apply.
/// </summary>
public sealed record ExplorerContext
{
    public required ExplorerViewType    ViewType             { get; init; }
    public          string?             CurrentPath          { get; init; }
    public          string?             Description          { get; init; }
    public IReadOnlyList<string>        Observations         { get; init; } = [];
    public IReadOnlyList<string>        WorkflowSuggestions  { get; init; } = [];
}

/// <summary>
/// Windows Settings page interpretation.
/// </summary>
public sealed record SettingsPageContext
{
    public required SettingsPageType    PageType        { get; init; }
    public          string?             PageTitle       { get; init; }
    public          string?             Description     { get; init; }
    public IReadOnlyList<string>        GuidanceSteps   { get; init; } = [];
    public IReadOnlyList<string>        RelatedActions  { get; init; } = [];
}

/// <summary>
/// A captured frame from the screen capture coordinator.
/// Raw BGRA pixel data, width × height, auto-expiring.
/// NEVER persisted to disk unless explicitly bookmarked by the user.
/// </summary>
public sealed record CapturedFrame
{
    public required int              Width     { get; init; }
    public required int              Height    { get; init; }

    /// <summary>Raw BGRA32 pixel bytes. Width × Height × 4 bytes.</summary>
    public required byte[]           PixelData { get; init; }

    public required DateTimeOffset   CapturedAt { get; init; }

    /// <summary>
    /// Auto-expiry timestamp. After this point the frame should be discarded.
    /// Default: 5 minutes after capture.
    /// </summary>
    public required DateTimeOffset   ExpiresAt  { get; init; }

    public          string?          SourceWindowTitle { get; init; }
    public          bool             IsExpired => DateTimeOffset.Now >= ExpiresAt;
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / response types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Initiates a visual analysis request with stated reason and mode.
/// All requests must pass through ConsentBoundScreenAnalysis before execution.
/// </summary>
public sealed record ScreenAnalysisRequest
{
    public required VisualAnalysisMode Mode         { get; init; }
    public required string             Reason       { get; init; }

    /// <summary>"companion", "investigation", "user", "autonomy"</summary>
    public          string?            RequestedBy  { get; init; }
}

/// <summary>
/// Result of a consented visual analysis pass.
/// If Approved == false, all other fields are null.
/// </summary>
public sealed record ScreenAnalysisResult
{
    public required bool                            Approved        { get; init; }
    public          SemanticWindowContext?           WindowContext   { get; init; }
    public          ExplorerContext?                 ExplorerContext { get; init; }
    public          SettingsPageContext?             SettingsContext { get; init; }
    public          IReadOnlyList<VisualWorkflowTarget>? Targets    { get; init; }
    public          CapturedFrame?                  Frame           { get; init; }
    public          string?                         AnalysisSummary { get; init; }
    public          DateTimeOffset?                 ExpiresAt       { get; init; }

    public static readonly ScreenAnalysisResult Denied =
        new() { Approved = false };
}

// ─────────────────────────────────────────────────────────────────────────────
// Consent event args
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Raised by ConsentBoundScreenAnalysis on the UI thread when visual analysis
/// requires user approval. Subscribe in the CompanionOverlayWindow to show the
/// consent card. Call Approve() or Deny() from the UI thread.
/// </summary>
public sealed class VisualAnalysisConsentEventArgs : EventArgs
{
    public required VisualAnalysisMode Mode          { get; init; }
    public required string             Reason        { get; init; }
    public required string             RequestedBy   { get; init; }

    /// <summary>Call from UI thread to approve the analysis.</summary>
    public Action? Approve { get; init; }

    /// <summary>Call from UI thread to deny the analysis.</summary>
    public Action? Deny    { get; init; }
}

/// <summary>
/// Raised by IVisualContextService when the semantic window context changes.
/// </summary>
public sealed class VisualContextChangedEventArgs : EventArgs
{
    public required SemanticWindowContext Context    { get; init; }
    public          ExplorerContext?      Explorer   { get; init; }
    public          SettingsPageContext?  Settings   { get; init; }
}

/// <summary>
/// Guided interaction step — describes one step in a visual workflow guide.
/// </summary>
public sealed record GuidedVisualStep
{
    public required string     StepTitle     { get; init; }
    public required string     Instruction   { get; init; }
    public          string?    TargetLabel   { get; init; }
    public          ScreenRect TargetRect    { get; init; }
    public          string?    DirectionHint { get; init; }  // "top-right", "bottom", etc.
    public          bool       RequiresConfirmation { get; init; }
    public static string NewId() => Guid.NewGuid().ToString("N");
}
