using System.Runtime.InteropServices;
using Lucid.Services.Timeline;
using Lucid.Services.VisualContext;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Lucid.Views;

/// <summary>
/// Guided Interaction Overlay — Phase 18B
///
/// A compact, always-on-top floating panel that walks the user through
/// operational UI interactions one step at a time.
///
/// Features:
///   • Step title, instruction, direction arrow, target label
///   • Progress bar across the full step sequence
///   • Next / Back / Done navigation
///   • Draggable via the header
///   • Positioned intelligently near the target element's screen rect
///   • Fires StepAdvanced and GuideCancelled events for host coordination
///
/// Safety rules (non-negotiable):
///   • NEVER auto-clicks on the user's behalf.
///   • NEVER stays open without an active workflow (auto-closes on Done/Cancel).
///   • Does NOT capture any screen pixels — guidance only.
///   • The user can dismiss at any time via the close (✕) button.
///
/// Threading: all public methods must be called on the UI thread.
/// </summary>
public sealed partial class GuidedInteractionOverlay : Window
{
    // ── Window sizing ─────────────────────────────────────────────────────────

    private const int WindowWidth = 320;

    // ── State ─────────────────────────────────────────────────────────────────

    private IReadOnlyList<GuidedVisualStep> _steps       = [];
    private int                             _currentIndex = 0;
    private string                          _workflowTitle = string.Empty;

    // ── AppWindow / presenter ─────────────────────────────────────────────────

    private readonly AppWindow           _appWindow;
    private readonly OverlappedPresenter _presenter;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcher;

    // ── Drag state ────────────────────────────────────────────────────────────

    private bool       _isDragging;
    private PointInt32 _dragStartCursor;
    private PointInt32 _dragStartWindowPos;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised when the user advances to the next step.</summary>
    public event EventHandler<int>? StepAdvanced;

    /// <summary>Raised when the user completes all steps.</summary>
    public event EventHandler? GuideCompleted;

    /// <summary>Raised when the user cancels the guide (✕ button or Escape).</summary>
    public event EventHandler? GuideCancelled;

    // ── Constructor ───────────────────────────────────────────────────────────

    public GuidedInteractionOverlay()
    {
        InitializeComponent();

        _uiDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // ── AppWindow: always-on-top, frameless, non-resizable ────────────
        _appWindow = GetAppWindowForCurrentWindow();

        _presenter = OverlappedPresenter.Create();
        _presenter.IsAlwaysOnTop  = true;
        _presenter.IsResizable    = false;
        _presenter.IsMaximizable  = false;
        _presenter.IsMinimizable  = false;
        _appWindow.SetPresenter(_presenter);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        Closed += (_, _) => GuideCancelled?.Invoke(this, EventArgs.Empty);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a new guided workflow and shows the first step.
    /// Must be called on the UI thread.
    /// </summary>
    public void StartWorkflow(
        IReadOnlyList<GuidedVisualStep> steps,
        string                         workflowTitle)
    {
        if (steps.Count == 0) return;

        _steps         = steps;
        _workflowTitle = workflowTitle;
        _currentIndex  = 0;

        // Record the workflow start now — the overlay is the single owner of
        // timeline state for guided workflows, so we emit the start event here
        // rather than in a separate service call.
        AppServices.Timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = TimelineEventType.VisualWorkflowStarted,
            OccurredAt = DateTimeOffset.Now,
            Title      = $"Visual workflow started: {workflowTitle}",
            Detail     = $"{steps.Count} guided step{(steps.Count == 1 ? "" : "s")}",
            Severity   = TimelineEventSeverity.Info,
        });

        ApplyStep(0);
        PositionWindow(steps[0].TargetRect);
        _appWindow.Show();
    }

    /// <summary>
    /// Advances to the given step index without user interaction.
    /// Used when the host drives the workflow externally.
    /// </summary>
    public void GoToStep(int index)
    {
        if (index < 0 || index >= _steps.Count) return;
        _currentIndex = index;
        ApplyStep(index);
        PositionWindow(_steps[index].TargetRect);
    }

    /// <summary>Hides the overlay without raising GuideCancelled.</summary>
    public void HideOverlay() => _appWindow.Hide();

    // ── Step rendering ────────────────────────────────────────────────────────

    private void ApplyStep(int index)
    {
        var step  = _steps[index];
        var total = _steps.Count;
        var isLast = index == total - 1;

        // Header
        GuideTitleText.Text  = _workflowTitle;
        StepBadgeText.Text   = $"Step {index + 1}";
        StepCounterText.Text = $"{index + 1} / {total}";

        // Step content
        StepTitleText.Text  = step.StepTitle;
        InstructionText.Text = step.Instruction;

        // Target indicator
        if (!string.IsNullOrEmpty(step.TargetLabel) || !step.TargetRect.IsEmpty)
        {
            TargetIndicator.Visibility  = Visibility.Visible;
            TargetLabelText.Text        = step.TargetLabel ?? "Target element";
            DirectionArrow.Text         = ArrowForDirection(step.DirectionHint);
            TargetPositionText.Text     = step.TargetRect.IsEmpty
                ? string.Empty
                : $"Position: {step.TargetRect.X}, {step.TargetRect.Y}";
        }
        else
        {
            TargetIndicator.Visibility = Visibility.Collapsed;
        }

        // Progress bar
        StepProgressBar.Value = total > 1
            ? (double)index / (total - 1) * 100.0
            : 100.0;

        // Navigation buttons
        PrevButton.Visibility   = index > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButtonLabel.Text    = isLast ? "Done ✓" : "Next →";

        // Record timeline event
        AppServices.Timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = TimelineEventType.VisualTargetHighlighted,
            OccurredAt = DateTimeOffset.Now,
            Title      = $"Guide step {index + 1}/{total}: {step.StepTitle}",
            Detail     = step.Instruction,
            Severity   = TimelineEventSeverity.Info,
        });
    }

    // ── Navigation button handlers ────────────────────────────────────────────

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex >= _steps.Count - 1)
        {
            // Last step — complete the guide
            _appWindow.Hide();
            AppServices.Timeline.AddExternalEvent(new TimelineEvent
            {
                Id         = TimelineEvent.NewId(),
                Type       = TimelineEventType.GuidedVisualStepCompleted,
                OccurredAt = DateTimeOffset.Now,
                Title      = $"Visual guide completed: {_workflowTitle}",
                Severity   = TimelineEventSeverity.Good,
            });
            GuideCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _currentIndex++;
            ApplyStep(_currentIndex);
            PositionWindow(_steps[_currentIndex].TargetRect);
            StepAdvanced?.Invoke(this, _currentIndex);
        }
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex <= 0) return;
        _currentIndex--;
        ApplyStep(_currentIndex);
        PositionWindow(_steps[_currentIndex].TargetRect);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _appWindow.Hide();

        // Record cancellation — only emitted via user-initiated close (✕ button).
        // The OS Closed event is a separate path handled by the constructor handler.
        if (!string.IsNullOrEmpty(_workflowTitle))
        {
            AppServices.Timeline.AddExternalEvent(new TimelineEvent
            {
                Id         = TimelineEvent.NewId(),
                Type       = TimelineEventType.VisualWorkflowCancelled,
                OccurredAt = DateTimeOffset.Now,
                Title      = $"Visual guide cancelled: {_workflowTitle}",
                Severity   = TimelineEventSeverity.Info,
            });
        }

        GuideCancelled?.Invoke(this, EventArgs.Empty);
    }

    // ── Drag handle ───────────────────────────────────────────────────────────

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
        _appWindow.Move(new PointInt32(
            _dragStartWindowPos.X + (pt.X - _dragStartCursor.X),
            _dragStartWindowPos.Y + (pt.Y - _dragStartCursor.Y)));
    }

    private void DragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var el = (UIElement)sender;
        el.ReleasePointerCapture(e.Pointer);
        RootGrid.Opacity = 1.0;
        _isDragging      = false;
        e.Handled        = true;
    }

    // ── Intelligent positioning ───────────────────────────────────────────────

    /// <summary>
    /// Positions the overlay near the target element's screen rect.
    /// Prefers placing to the right; falls back to left if near screen edge.
    /// </summary>
    private void PositionWindow(ScreenRect targetRect)
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(
                _appWindow.Id, DisplayAreaFallback.Nearest);
            var work = displayArea.WorkArea;

            // Estimate window height (content-driven, approximate)
            const int estimatedHeight = 220;
            const int margin          = 16;

            int x, y;

            if (!targetRect.IsEmpty)
            {
                // Try to the right of the target
                x = targetRect.Right + margin;
                y = targetRect.Y;

                // If it would go off the right edge, try the left side
                if (x + WindowWidth > work.X + work.Width - margin)
                    x = targetRect.X - WindowWidth - margin;

                // Clamp to work area
                x = Math.Max(work.X + margin,
                    Math.Min(x, work.X + work.Width - WindowWidth - margin));
                y = Math.Max(work.Y + margin,
                    Math.Min(y, work.Y + work.Height - estimatedHeight - margin));
            }
            else
            {
                // No target rect — bottom-right of work area
                x = work.X + work.Width  - WindowWidth  - margin;
                y = work.Y + work.Height - estimatedHeight - margin;
            }

            _appWindow.MoveAndResize(new RectInt32(x, y, WindowWidth, estimatedHeight));
        }
        catch { /* positioning is best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ArrowForDirection(string? hint) => hint?.ToLowerInvariant() switch
    {
        "top"          => "↑",
        "top-right"    => "↗",
        "right"        => "→",
        "bottom-right" => "↘",
        "bottom"       => "↓",
        "bottom-left"  => "↙",
        "left"         => "←",
        "top-left"     => "↖",
        _              => "→",
    };

    private AppWindow GetAppWindowForCurrentWindow()
    {
        var hWnd  = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var winId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        return AppWindow.GetFromWindowId(winId);
    }
}
