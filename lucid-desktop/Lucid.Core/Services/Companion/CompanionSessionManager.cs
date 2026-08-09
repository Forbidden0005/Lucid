namespace Lucid.Services.Companion;

/// <summary>
/// In-memory companion session state manager.
///
/// Owns the overlay visibility state (Hidden / Bubble / Expanded),
/// pin state, and last docking preference (scaffold for future snap-to-edge).
///
/// Does NOT own conversation messages — those live in CompanionChatViewModel.
///
/// Threading:
///   All state mutations must occur on the UI thread.
///   StateChanged fires synchronously on the calling thread.
///
/// Lifetime:
///   Registered in the composition root. Single instance for the application lifetime.
/// </summary>
public sealed class CompanionSessionManager : ICompanionSessionManager
{
    private CompanionOverlayState _currentState = CompanionOverlayState.Hidden;
    private bool                  _isPinned     = false;

    // ── Public state ───────────────────────────────────────────────────────────

    public CompanionOverlayState CurrentState => _currentState;

    public bool IsExpanded => _currentState == CompanionOverlayState.Expanded;

    public bool IsPinned => _isPinned;

    // ── Events ─────────────────────────────────────────────────────────────────

    public event EventHandler<CompanionStateChangedEventArgs>? StateChanged;

    // ── State transitions ──────────────────────────────────────────────────────

    /// <summary>Makes the companion visible. Restores to Bubble state.</summary>
    public void Show()
    {
        if (_currentState != CompanionOverlayState.Hidden) return;
        SetState(CompanionOverlayState.Bubble);
    }

    /// <summary>Hides the companion window.</summary>
    public void Hide()
    {
        if (_currentState == CompanionOverlayState.Hidden) return;
        SetState(CompanionOverlayState.Hidden);
    }

    /// <summary>
    /// Toggles between Bubble and Expanded.
    /// If Hidden, switches to Expanded first.
    /// </summary>
    public void ToggleExpanded()
    {
        var next = _currentState switch
        {
            CompanionOverlayState.Hidden   => CompanionOverlayState.Expanded,
            CompanionOverlayState.Bubble   => CompanionOverlayState.Expanded,
            CompanionOverlayState.Expanded => CompanionOverlayState.Bubble,
            _                              => CompanionOverlayState.Expanded,
        };
        SetState(next);
    }

    /// <summary>Collapses to bubble mode.</summary>
    public void CollapseToBubble()
    {
        if (_currentState == CompanionOverlayState.Bubble) return;
        SetState(CompanionOverlayState.Bubble);
    }

    /// <summary>Expands to full panel.</summary>
    public void Expand()
    {
        if (_currentState == CompanionOverlayState.Expanded) return;
        SetState(CompanionOverlayState.Expanded);
    }

    /// <summary>Sets IsPinned = true.</summary>
    public void Pin()
    {
        if (_isPinned) return;
        _isPinned = true;
        RaiseStateChanged(_currentState, _currentState);
    }

    /// <summary>Sets IsPinned = false.</summary>
    public void Unpin()
    {
        if (!_isPinned) return;
        _isPinned = false;
        RaiseStateChanged(_currentState, _currentState);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void SetState(CompanionOverlayState newState)
    {
        var previous  = _currentState;
        _currentState = newState;
        RaiseStateChanged(previous, newState);
    }

    private void RaiseStateChanged(
        CompanionOverlayState previous,
        CompanionOverlayState current)
    {
        StateChanged?.Invoke(this, new CompanionStateChangedEventArgs
        {
            PreviousState = previous,
            NewState      = current,
            IsPinned      = _isPinned,
        });
    }
}
