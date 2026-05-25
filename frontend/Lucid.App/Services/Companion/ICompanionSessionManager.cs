namespace Lucid.Services.Companion;

/// <summary>
/// Overlay state for the Companion window.
/// </summary>
public enum CompanionOverlayState
{
    /// <summary>Window is not visible.</summary>
    Hidden   = 0,

    /// <summary>Minimized to a floating bubble icon.</summary>
    Bubble   = 1,

    /// <summary>Full conversation panel is visible.</summary>
    Expanded = 2,
}

/// <summary>
/// Manages the runtime state of the Companion Overlay Window.
///
/// This service owns:
///   • Overlay visibility state (Hidden / Bubble / Expanded)
///   • Pin state (whether the companion stays visible across focus changes)
///   • Last known docking preference (scaffold for future snap-to-edge)
///
/// It does NOT own conversation messages — those live in CompanionChatViewModel.
///
/// Threading:
///   All state mutations must occur on the UI thread.
///   StateChanged fires on the calling thread.
///
/// Lifetime:
///   Registered in AppServices. Single instance for the application lifetime.
/// </summary>
public interface ICompanionSessionManager
{
    /// <summary>Current overlay display state.</summary>
    CompanionOverlayState CurrentState { get; }

    /// <summary>True when the overlay panel is in Expanded mode.</summary>
    bool IsExpanded { get; }

    /// <summary>
    /// True when the companion is pinned.
    /// A pinned companion stays visible and always-on-top.
    /// An unpinned companion may auto-hide in future phases.
    /// </summary>
    bool IsPinned { get; }

    /// <summary>Raised when CurrentState, IsExpanded, or IsPinned changes.</summary>
    event EventHandler<CompanionStateChangedEventArgs>? StateChanged;

    /// <summary>Makes the companion visible. Restores the last non-Hidden state.</summary>
    void Show();

    /// <summary>Hides the companion window.</summary>
    void Hide();

    /// <summary>
    /// Toggles between Bubble and Expanded.
    /// If Hidden, switches to Expanded first.
    /// </summary>
    void ToggleExpanded();

    /// <summary>Collapses to bubble mode.</summary>
    void CollapseToBubble();

    /// <summary>Expands to full panel.</summary>
    void Expand();

    /// <summary>Sets IsPinned = true.</summary>
    void Pin();

    /// <summary>Sets IsPinned = false.</summary>
    void Unpin();
}

/// <summary>
/// Event data for companion state changes.
/// Carries both the previous and new states for transition animation decisions.
/// </summary>
public sealed class CompanionStateChangedEventArgs : EventArgs
{
    public CompanionOverlayState PreviousState { get; init; }
    public CompanionOverlayState NewState      { get; init; }
    public bool                  IsPinned      { get; init; }
}
