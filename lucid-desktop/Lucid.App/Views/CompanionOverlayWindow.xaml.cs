using System.Runtime.InteropServices;
using Lucid.Services.Autonomy;
using Lucid.Services.Companion;
using Lucid.Services.DesktopContext;
using Lucid.Services.VisualContext;
using Lucid.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Lucid.Views;

/// <summary>
/// Companion Overlay Window — a frameless, always-on-top panel that surfaces
/// operational presence without forcing the user to switch applications.
///
/// Window management:
///   • Always-on-top via OverlappedPresenter.IsAlwaysOnTop
///   • Frameless (no OS title bar) — content extends into the title area
///   • Drag is handled manually via P/Invoke GetCursorPos; SetTitleBar(null)
///   • Window opacity drops to 0.85 while dragging, restores on release
///   • Snap-to-edge: scaffold in place (full implementation: future phase)
///   • Size driven by ICompanionSessionManager.StateChanged events
///
/// Lifetime:
///   Created from MainWindow on first companion toggle.
///   Hidden/shown via AppWindow.Hide()/Show() in response to state changes.
///   Closed event unsubscribes from state manager.
///
/// Threading:
///   StateChanged handler marshals to UI thread before mutating any UI state.
///   All other methods assume UI-thread caller.
///
/// Phase 17A:
///   Placeholder messages only — OperationalConversationEngine not wired.
/// </summary>
public sealed partial class CompanionOverlayWindow : Window
{
    public CompanionChatViewModel ViewModel { get; }

    private readonly AppWindow            _appWindow;
    private readonly OverlappedPresenter  _presenter;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcher;

    // ── Window sizing constants ────────────────────────────────────────────────

    private static readonly SizeInt32 BubbleSize   = new(72,  72);
    private static readonly SizeInt32 ExpandedSize = new(380, 600);

    // ── Snap-to-edge scaffold ─────────────────────────────────────────────────
    //    Full implementation planned for a future phase.

    private const int SnapThreshold  = 32;  // px from screen edge to trigger snap
    private const int SnapEdgeMargin = 16;  // px gap from edge when snapped

    // ── Drag state ────────────────────────────────────────────────────────────

    private bool       _isDragging;
    private PointInt32 _dragStartCursor;
    private PointInt32 _dragStartWindowPos;

    // ── Workflow review gate (Phase 17F) ──────────────────────────────────────
    //    Stored when HumanReviewGate raises ReviewRequired so that the
    //    Approve / Decline buttons can unblock the awaiting RequestReviewAsync.

    private Action? _approveCallback;
    private Action? _denyCallback;

    // ── Visual consent gate (Phase 18B) ───────────────────────────────────────
    //    Stored when ConsentBoundScreenAnalysis raises ConsentRequired so that
    //    the Allow Once / Deny buttons can unblock the awaiting RequestConsentAsync.

    private Action? _visualApproveCallback;
    private Action? _visualDenyCallback;

    // ── Guided interaction overlay (Phase 18B) ────────────────────────────────
    //    Lazily created on first visual guide request. Kept as a field so we can
    //    re-use the same window instance across multiple guides in one session.
    //    _currentGuidedWorkflowTitle is updated on every StartWorkflow call so the
    //    GuideCompleted handler always narrates the correct (most recent) title.

    private GuidedInteractionOverlay? _guidedOverlay;
    private string _currentGuidedWorkflowTitle = string.Empty;

    // ── Win32 P/Invoke for reliable screen-pixel cursor position ──────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    // ── Constructor ────────────────────────────────────────────────────────────

    public CompanionOverlayWindow()
    {
        // Build ViewModel before InitializeComponent so x:Bind can resolve it.
        ViewModel = new CompanionChatViewModel(
            AppServices.LlmChat,
            AppServices.AutomationOrchestrator,
            AppServices.Logger);
        InitializeComponent();

        _uiDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // ── AppWindow setup ────────────────────────────────────────────────
        _appWindow = GetAppWindowForCurrentWindow();

        _presenter = OverlappedPresenter.Create();
        _presenter.IsAlwaysOnTop  = true;
        _presenter.IsResizable    = false;
        _presenter.IsMaximizable  = false;
        _presenter.IsMinimizable  = false;
        _appWindow.SetPresenter(_presenter);

        // Remove the native title bar AND its caption buttons. Without this,
        // ExtendsContentIntoTitleBar alone leaves the OS close button floating
        // over the overlay's own header — two overlapping X's, and the native
        // one destroys the window instead of hiding it to the bubble.
        _presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);

        // Frameless — content fills the full client area.
        // SetTitleBar(null) disables OS-managed drag so we can manage it manually.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        // ── Apply initial state ────────────────────────────────────────────
        // Seed from the session manager state at construction time.
        // MainWindow calls ToggleExpanded() immediately after construction,
        // which will fire StateChanged and resize accordingly.
        var initialState = AppServices.CompanionSession.CurrentState;
        ApplyState(initialState == CompanionOverlayState.Hidden
            ? CompanionOverlayState.Expanded
            : initialState);

        RestoreOrDefaultPosition();

        // ── Subscribe to state changes ─────────────────────────────────────
        AppServices.CompanionSession.StateChanged += OnSessionStateChanged;

        // ── Subscribe to desktop context changes (Phase 17B) ──────────────
        AppServices.DesktopContext.ContextChanged += OnDesktopContextChanged;

        // ── Subscribe to visual consent events (Phase 18B) ───────────────
        AppServices.VisualConsent.ConsentRequired += OnVisualConsentRequired;

        // ── Subscribe to autonomous workflow events (Phase 17F) ───────────
        AppServices.AutonomousWorkflowEngine.WorkflowPlanned   += OnWorkflowPlanned;
        AppServices.AutonomousWorkflowEngine.WorkflowProgress  += OnWorkflowProgress;
        AppServices.AutonomousWorkflowEngine.WorkflowCompleted += OnWorkflowCompleted;
        AppServices.AutonomousWorkflowEngine.GoalUnresolved    += OnGoalUnresolved;
        AppServices.HumanReviewGate.ReviewRequired             += OnReviewRequired;

        Closed += (_, _) =>
        {
            AppServices.CompanionSession.StateChanged -= OnSessionStateChanged;
            AppServices.DesktopContext.ContextChanged -= OnDesktopContextChanged;
            AppServices.VisualConsent.ConsentRequired -= OnVisualConsentRequired;

            AppServices.AutonomousWorkflowEngine.WorkflowPlanned   -= OnWorkflowPlanned;
            AppServices.AutonomousWorkflowEngine.WorkflowProgress  -= OnWorkflowProgress;
            AppServices.AutonomousWorkflowEngine.WorkflowCompleted -= OnWorkflowCompleted;
            AppServices.AutonomousWorkflowEngine.GoalUnresolved    -= OnGoalUnresolved;
            AppServices.HumanReviewGate.ReviewRequired             -= OnReviewRequired;

            // If the window was destroyed by the OS (Alt+F4, task-kill, etc.) rather
            // than via CloseButton_Click, the session manager still thinks the overlay
            // is visible. Reset to Hidden so the state is consistent and MainWindow
            // can create a fresh window on the next companion toggle.
            if (AppServices.CompanionSession.CurrentState != CompanionOverlayState.Hidden)
                AppServices.CompanionSession.Hide();

            // Hide the guided overlay if it was open — it has no owner relationship
            // so it survives on its own; we just hide it cleanly.
            try { _guidedOverlay?.HideOverlay(); } catch { /* best-effort */ }
        };
    }

    // ── State change handling ──────────────────────────────────────────────────

    private void OnSessionStateChanged(object? sender, CompanionStateChangedEventArgs e)
    {
        if (!_uiDispatcher.HasThreadAccess)
        {
            _uiDispatcher.TryEnqueue(() => OnSessionStateChanged(sender, e));
            return;
        }

        if (e.NewState == CompanionOverlayState.Hidden)
        {
            _appWindow.Hide();
            return;
        }

        _appWindow.Show();
        ApplyState(e.NewState);
    }

    private void ApplyState(CompanionOverlayState state)
    {
        ViewModel.OverlayState = state;

        var size = state switch
        {
            CompanionOverlayState.Bubble   => BubbleSize,
            CompanionOverlayState.Expanded => ExpandedSize,
            _                              => BubbleSize,
        };

        _appWindow.Resize(size);

        if (state == CompanionOverlayState.Expanded)
            ScrollToLatestMessage();
    }

    // ── Bubble: drag + tap-to-expand ─────────────────────────────────────────
    //    Pointer events on the outer Grid are used for both drag and click.
    //    If the pointer moves more than 5px it's a drag; otherwise it's a tap.

    private void BubbleRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var el = (UIElement)sender;
        el.CapturePointer(e.Pointer);
        RootGrid.Opacity = 0.85;

        GetCursorPos(out POINT pt);
        _dragStartCursor    = new PointInt32(pt.X, pt.Y);
        _dragStartWindowPos = _appWindow.Position;
        _isDragging         = false;
        e.Handled           = true;
    }

    private void BubbleRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!e.Pointer.IsInContact) return;

        GetCursorPos(out POINT pt);
        var dx = pt.X - _dragStartCursor.X;
        var dy = pt.Y - _dragStartCursor.Y;

        if (!_isDragging && (Math.Abs(dx) > 5 || Math.Abs(dy) > 5))
            _isDragging = true;

        if (_isDragging)
        {
            _appWindow.Move(new PointInt32(
                _dragStartWindowPos.X + dx,
                _dragStartWindowPos.Y + dy));
        }
    }

    private void BubbleRoot_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var el = (UIElement)sender;
        el.ReleasePointerCapture(e.Pointer);
        RootGrid.Opacity = 1.0;

        if (!_isDragging)
        {
            // Tap on bubble → expand to full panel
            AppServices.CompanionSession.Expand();
        }
        else
        {
            // Drag ended — snap to edge if close
            SnapToEdgeIfClose();
        }

        _isDragging = false;
        e.Handled   = true;
    }

    // ── Expanded: header drag handle ─────────────────────────────────────────

    private void DragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var el = (UIElement)sender;
        el.CapturePointer(e.Pointer);
        RootGrid.Opacity = 0.85;

        GetCursorPos(out POINT pt);
        _dragStartCursor    = new PointInt32(pt.X, pt.Y);
        _dragStartWindowPos = _appWindow.Position;
        _isDragging         = true;
        e.Handled           = true;
    }

    private void DragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || !e.Pointer.IsInContact) return;

        GetCursorPos(out POINT pt);
        var dx = pt.X - _dragStartCursor.X;
        var dy = pt.Y - _dragStartCursor.Y;

        _appWindow.Move(new PointInt32(
            _dragStartWindowPos.X + dx,
            _dragStartWindowPos.Y + dy));
    }

    private void DragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var el = (UIElement)sender;
        el.ReleasePointerCapture(e.Pointer);
        RootGrid.Opacity = 1.0;
        _isDragging      = false;

        SnapToEdgeIfClose();
        e.Handled = true;
    }

    // ── Snap-to-edge scaffold ─────────────────────────────────────────────────
    //    Detects proximity to screen edges and nudges the window to a clean
    //    margin if within SnapThreshold. Full animation planned for a later phase.

    private void SnapToEdgeIfClose()
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(
                _appWindow.Id, DisplayAreaFallback.Nearest);
            var work = displayArea.WorkArea;
            var pos  = _appWindow.Position;
            var size = _appWindow.Size;

            var x = pos.X;
            var y = pos.Y;

            // Horizontal snap
            if (pos.X < work.X + SnapThreshold)
                x = work.X + SnapEdgeMargin;
            else if (pos.X + size.Width > work.X + work.Width - SnapThreshold)
                x = work.X + work.Width - size.Width - SnapEdgeMargin;

            // Vertical snap
            if (pos.Y < work.Y + SnapThreshold)
                y = work.Y + SnapEdgeMargin;
            else if (pos.Y + size.Height > work.Y + work.Height - SnapThreshold)
                y = work.Y + work.Height - size.Height - SnapEdgeMargin;

            if (x != pos.X || y != pos.Y)
                _appWindow.Move(new PointInt32(x, y));

            // Persist the final snapped position so it is restored on next launch.
            SavePositionAsync();
        }
        catch
        {
            // Position fails silently if display info is unavailable.
        }

        // Refresh contextual suggestion chips whenever desktop focus changes (Phase 17C).
        // Best-effort - never blocks the UI or surfaces an error.
        ViewModel.RefreshContextualSuggestions();
    }

    // ── Position persistence ───────────────────────────────────────────────────

    /// <summary>
    /// Fire-and-forget: saves the current window position to app settings.
    /// Called after every drag-end snap so the position survives restarts.
    /// </summary>
    private async void SavePositionAsync()
    {
        try
        {
            var pos     = _appWindow.Position;
            var updated = AppServices.Settings.Current with
            {
                CompanionPositionX = pos.X,
                CompanionPositionY = pos.Y,
            };
            await AppServices.Settings.SaveAsync(updated);
        }
        catch
        {
            // Best-effort — a failed save is not user-visible.
        }
    }

    // ── Initial window position ────────────────────────────────────────────────

    /// <summary>
    /// Restores the last saved companion position from settings, falling back
    /// to the default bottom-right position near the taskbar if no position has
    /// been saved yet or if the saved position is no longer on-screen.
    /// </summary>
    private void RestoreOrDefaultPosition()
    {
        try
        {
            var s = AppServices.Settings.Current;
            if (s.CompanionPositionX.HasValue && s.CompanionPositionY.HasValue)
            {
                // Validate the saved position is still on a connected display.
                var savedPos  = new PointInt32(s.CompanionPositionX.Value, s.CompanionPositionY.Value);
                var area      = DisplayArea.GetFromPoint(savedPos, DisplayAreaFallback.None);
                if (area is not null)
                {
                    _appWindow.Move(savedPos);
                    return;
                }
                // Saved position is off-screen — fall through to default.
            }
        }
        catch { }

        PositionNearTaskbar();
    }

    private void PositionNearTaskbar()
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(
                _appWindow.Id, DisplayAreaFallback.Nearest);
            var work    = displayArea.WorkArea;
            var winSize = _appWindow.Size;

            // Bottom-right, SnapEdgeMargin from edge
            var x = work.X + work.Width  - winSize.Width  - SnapEdgeMargin;
            var y = work.Y + work.Height - winSize.Height - SnapEdgeMargin;

            _appWindow.Move(new PointInt32(x, y));
        }
        catch { }
    }

    // ── Header button handlers ────────────────────────────────────────────────

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
        => AppServices.CompanionSession.CollapseToBubble();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        AppServices.CompanionSession.Hide();
        // OnSessionStateChanged will call _appWindow.Hide()
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearConversationCommand.Execute(null);

    private void RetryButton_Click(object sender, RoutedEventArgs e)
        => ViewModel.RetryLlmCheckCommand.Execute(null);

    // ── Navigation event (Phase 17C) ─────────────────────────────────────────
    //    Fired when a suggested-action chip is tapped in a companion response.
    //    MainWindow subscribes to this and calls NavigateToPage(tag).
    //    The window itself never navigates — single responsibility.

    public event EventHandler<string>? NavigationRequested;

    // ── Quick action click (DataTemplate command bridge) ─────────────────────
    //    x:Bind cannot resolve ViewModel commands from inside a DataTemplate
    //    whose DataContext is the item. Tag carries the QuickAction reference.

    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: QuickAction qa })
            ViewModel.ExecuteQuickActionCommand.Execute(qa);
    }

    // ── Suggested action click (Phase 17C response card navigation) ──────────
    //    Raised by tapping a SuggestedAction chip inside a system message card.
    //    Tag is set in XAML to the SuggestedAction record via x:Bind.

    private void SuggestedAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Lucid.Services.Companion.SuggestedAction action }) return;
        if (string.IsNullOrEmpty(action.NavigationTag)) return;

        // Phase 17D: automation: prefix routes to the orchestrator instead of navigation.
        if (action.IsAutomationAction && action.AutomationTarget is { } autoTarget)
        {
            // Planning/execution/event wiring live in the ViewModel — the
            // window only routes the action tag (no business logic in views).
            ViewModel.LaunchAutomation(autoTarget);
            return;
        }

        // Phase 18B: visual: prefix routes to the visual context service.
        if (action.IsVisualAction && action.VisualActionTarget is { } visualTarget)
        {
            LaunchVisualAction(visualTarget);
            return;
        }

        NavigationRequested?.Invoke(this, action.NavigationTag);
    }

    // ── Input box ─────────────────────────────────────────────────────────────

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && !e.KeyStatus.IsMenuKeyDown)
        {
            e.Handled = true;
            ViewModel.SendMessageCommand.Execute(null);
            ScrollToLatestMessage();
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SendMessageCommand.Execute(null);
        ScrollToLatestMessage();
    }

    // ── Scroll to latest message ──────────────────────────────────────────────

    private void ScrollToLatestMessage()
    {
        // Deferred scroll so the ItemsRepeater finishes layout first.
        _ = _uiDispatcher.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => MessageScroller.ChangeView(null, double.MaxValue, null, true));
    }

    // ── Desktop context awareness (Phase 17B) ────────────────────────────────

    private void OnDesktopContextChanged(object? sender, ContextChangedEventArgs e)
    {
        // Already on UI thread (DesktopContextService fires on DispatcherQueue)
        var snap = e.Current;

        if (!string.IsNullOrEmpty(snap.CurrentOperationalFocus))
        {
            ViewModel.ContextBannerText = snap.CurrentOperationalFocus;
            ViewModel.ContextGlyph = snap.ActiveWindow?.AppCategory switch
            {
                AppCategory.Explorer => "",    // folder glyph
                AppCategory.Browser  => "",    // web glyph
                AppCategory.Editor   => "",    // edit glyph
                AppCategory.Terminal => "",    // terminal glyph
                AppCategory.Media    => "",    // media glyph
                AppCategory.Settings => "",    // settings glyph
                _                    => "",    // info glyph
            };
        }
    }

    // ── Phase 17F: Autonomous Workflow event handlers ─────────────────────────

    /// <summary>
    /// Raised on the thread pool when a workflow is planned and ready to begin.
    /// Shows the workflow panel and seeds the initial title/step count.
    /// </summary>
    private void OnWorkflowPlanned(object? sender, AutonomousWorkflow workflow)
    {
        _uiDispatcher.TryEnqueue(() =>
        {
            WorkflowTitleText.Text     = workflow.Title;
            WorkflowProgressText.Text  = $"Step 0 of {workflow.Steps.Count}";
            WorkflowProgressBar.Value  = 0;
            WorkflowNarrationText.Text = $"Planning complete · {workflow.Steps.Count} steps";
            WorkflowReviewPanel.Visibility = Visibility.Collapsed;
            WorkflowPanel.Visibility   = Visibility.Visible;
        });
    }

    /// <summary>
    /// Raised on the thread pool after each step transition.
    /// Updates progress bar, step fraction, and narration text.
    /// </summary>
    private void OnWorkflowProgress(object? sender, WorkflowProgressEventArgs args)
    {
        _uiDispatcher.TryEnqueue(() =>
        {
            WorkflowProgressText.Text  = $"Step {args.CurrentStep} of {args.TotalSteps}";
            WorkflowNarrationText.Text = args.Narration;
            WorkflowProgressBar.Value  = args.TotalSteps > 0
                ? (double)args.CurrentStep / args.TotalSteps * 100.0
                : 0.0;

            // Collapse the review panel when the step that triggered it has concluded.
            if (args.IsCompleted || args.IsFailed)
                WorkflowReviewPanel.Visibility = Visibility.Collapsed;
        });
    }

    /// <summary>
    /// Raised on the thread pool when the workflow finishes (any terminal status).
    /// Hides the workflow panel and appends a completion/failure summary to chat.
    /// </summary>
    private void OnWorkflowCompleted(object? sender, WorkflowCompletedEventArgs args)
    {
        _uiDispatcher.TryEnqueue(() =>
        {
            WorkflowPanel.Visibility       = Visibility.Collapsed;
            WorkflowReviewPanel.Visibility = Visibility.Collapsed;
            _approveCallback = null;
            _denyCallback    = null;

            var summary = args.FinalStatus == WorkflowLifecycleStatus.Failed
                ? $"Workflow failed. {args.FailureReason ?? "An unexpected error occurred."}"
                : args.Summary;

            ViewModel.AddAutomationNarration(summary,
                isWarning: args.FinalStatus == WorkflowLifecycleStatus.Failed);
        });
    }

    /// <summary>
    /// Raised on the thread pool when the user's request could not be resolved
    /// to a supported operational goal.
    /// </summary>
    private void OnGoalUnresolved(object? sender, string message)
    {
        _uiDispatcher.TryEnqueue(() =>
            ViewModel.AddAutomationNarration(message, isWarning: true));
    }

    /// <summary>
    /// Raised on the UI thread by HumanReviewGate before any destructive step executes.
    /// Stores approve/deny callbacks and shows the review sub-panel.
    /// </summary>
    private void OnReviewRequired(object? sender, WorkflowReviewEventArgs args)
    {
        // HumanReviewGate already dispatches to the UI thread before raising.
        _approveCallback           = args.Approve;
        _denyCallback              = args.Deny;
        ReviewNarrationText.Text   = args.ReviewNarration;
        WorkflowReviewPanel.Visibility = Visibility.Visible;
    }

    private void CancelWorkflowButton_Click(object sender, RoutedEventArgs e)
        => AppServices.AutonomousWorkflowEngine.CancelCurrentWorkflow();

    private void ApproveButton_Click(object sender, RoutedEventArgs e)
    {
        WorkflowReviewPanel.Visibility = Visibility.Collapsed;
        var cb = _approveCallback;
        _approveCallback = null;
        _denyCallback    = null;
        cb?.Invoke();
    }

    private void DenyButton_Click(object sender, RoutedEventArgs e)
    {
        WorkflowReviewPanel.Visibility = Visibility.Collapsed;
        var cb = _denyCallback;
        _approveCallback = null;
        _denyCallback    = null;
        cb?.Invoke();
    }

    // ── Phase 18B: Visual consent handlers ───────────────────────────────────

    /// <summary>
    /// Raised by ConsentBoundScreenAnalysis on the UI thread when visual analysis
    /// needs explicit user approval. Shows the consent card and stores callbacks.
    /// </summary>
    private void OnVisualConsentRequired(object? sender, VisualAnalysisConsentEventArgs args)
    {
        // ConsentBoundScreenAnalysis already dispatches to the UI thread before raising.
        _visualApproveCallback         = args.Approve;
        _visualDenyCallback            = args.Deny;
        VisualConsentReasonText.Text   = args.Reason;
        VisualConsentPanel.Visibility  = Visibility.Visible;
    }

    private void VisualAllowButton_Click(object sender, RoutedEventArgs e)
    {
        VisualConsentPanel.Visibility = Visibility.Collapsed;
        var cb = _visualApproveCallback;
        _visualApproveCallback = null;
        _visualDenyCallback    = null;
        cb?.Invoke();
    }

    private void VisualDenyButton_Click(object sender, RoutedEventArgs e)
    {
        VisualConsentPanel.Visibility = Visibility.Collapsed;
        var cb = _visualDenyCallback;
        _visualApproveCallback = null;
        _visualDenyCallback    = null;
        cb?.Invoke();
    }

    // ── Phase 18B: Visual action launch ───────────────────────────────────────

    /// <summary>
    /// Dispatches a visual context action.  All heavy work happens on the thread
    /// pool; the consent card is surfaced via the ConsentRequired event (already
    /// wired to OnVisualConsentRequired above).
    /// </summary>
    private void LaunchVisualAction(string target)
    {
        switch (target)
        {
            case "analyze-window":
                // Full consent-gated analysis: semantic + element detection + pixel capture.
                _ = Task.Run(async () =>
                {
                    var result = await AppServices.VisualContext
                        .AnalyzeCurrentWindowAsync(
                            "You asked to analyze the current window",
                            "companion")
                        .ConfigureAwait(false);

                    if (result.Approved)
                    {
                        var summary = result.AnalysisSummary
                            ?? AppServices.VisualContext.DescribeActiveWindow();
                        _uiDispatcher.TryEnqueue(() =>
                            ViewModel.AddAutomationNarration(summary, isWarning: false));
                    }
                });
                break;

            case "guide-window":
            case "locate-element":
                // Guide-only: semantic analysis + element detection, no pixel capture.
                _ = Task.Run(async () =>
                {
                    var result = await AppServices.VisualContext
                        .GuideAnalysisAsync(
                            "You asked for guided visual assistance",
                            "companion")
                        .ConfigureAwait(false);

                    if (result.Approved && result.WindowContext is { } ctx)
                    {
                        var steps = BuildGuidedSteps(ctx, target);
                        if (steps.Count > 0)
                        {
                            _uiDispatcher.TryEnqueue(() =>
                                ShowGuidedOverlay(steps, ctx.ContextDescription ?? ctx.WindowTitle));
                        }
                        else
                        {
                            // No targeted steps — surface a summary narration instead.
                            var summary = result.AnalysisSummary
                                ?? AppServices.VisualContext.DescribeActiveWindow();
                            _uiDispatcher.TryEnqueue(() =>
                                ViewModel.AddAutomationNarration(summary, isWarning: false));
                        }
                    }
                });
                break;

            default:
                _ = Task.Run(async () =>
                    await AppServices.VisualContext
                        .AnalyzeCurrentWindowAsync($"Visual analysis: {target}", "companion")
                        .ConfigureAwait(false));
                break;
        }
    }

    /// <summary>
    /// Shows the GuidedInteractionOverlay for the given steps.
    /// Must be called on the UI thread.
    /// </summary>
    private void ShowGuidedOverlay(IReadOnlyList<GuidedVisualStep> steps, string workflowTitle)
    {
        // Always update the title field before starting — the GuideCompleted handler
        // reads this field rather than capturing the local so it stays correct when
        // the same overlay instance is reused for a later workflow.
        _currentGuidedWorkflowTitle = workflowTitle;

        if (_guidedOverlay is null)
        {
            _guidedOverlay = new GuidedInteractionOverlay();

            // Read the field at event-fire time, not at subscription time.
            _guidedOverlay.GuideCompleted += (_, _) =>
                ViewModel.AddAutomationNarration(
                    $"Visual guide completed: {_currentGuidedWorkflowTitle}", isWarning: false);

            // GuideCancelled is a no-op here — the overlay already logs the
            // cancellation event and manages its own cleanup.
        }

        // GuidedInteractionOverlay is the single owner of guided-workflow timeline
        // events: it emits VisualWorkflowStarted in StartWorkflow, VisualTargetHighlighted
        // per step in ApplyStep, GuidedVisualStepCompleted on completion, and
        // VisualWorkflowCancelled on cancel.  No separate service call needed.
        _guidedOverlay.StartWorkflow(steps, workflowTitle);
    }

    /// <summary>
    /// Converts a semantic window context into GuidedVisualStep objects.
    /// Uses WorkflowHints from the analyzer for the step instructions.
    /// </summary>
    private static IReadOnlyList<GuidedVisualStep> BuildGuidedSteps(
        SemanticWindowContext ctx, string target)
    {
        var steps = new List<GuidedVisualStep>();

        // Startup-settings specific guide
        if (ctx.WindowType == SemanticWindowType.StartupSettings
            || target.Contains("startup", StringComparison.OrdinalIgnoreCase))
        {
            steps.Add(new GuidedVisualStep
            {
                StepTitle     = "Startup Apps list",
                Instruction   = "You are on the Startup Apps page. The list below shows all " +
                                "apps that launch at login. Each row has a toggle on the right.",
                TargetLabel   = "App list",
                DirectionHint = "bottom",
            });
            steps.Add(new GuidedVisualStep
            {
                StepTitle     = "Disable a startup app",
                Instruction   = "Tap the toggle next to any app you want to stop from auto-starting. " +
                                "Blue = enabled, grey = disabled.",
                TargetLabel   = "Toggle switch",
                DirectionHint = "right",
            });
            return steps;
        }

        // Generic: one step per WorkflowHint (max 3)
        foreach (var hint in ctx.WorkflowHints.Take(3))
        {
            steps.Add(new GuidedVisualStep
            {
                StepTitle     = ctx.ContextDescription ?? ctx.WindowTitle,
                Instruction   = hint,
                DirectionHint = "right",
            });
        }

        return steps;
    }

    // ── Native window helper ───────────────────────────────────────────────────

    private AppWindow GetAppWindowForCurrentWindow()
    {
        var hWnd  = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var winId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        return AppWindow.GetFromWindowId(winId);
    }
}
