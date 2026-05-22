using System.Runtime.InteropServices;
using ExplainMyPC.Services.Companion;
using ExplainMyPC.Services.DesktopContext;
using ExplainMyPC.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace ExplainMyPC.Views;

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

    // ── Win32 P/Invoke for reliable screen-pixel cursor position ──────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    // ── Constructor ────────────────────────────────────────────────────────────

    public CompanionOverlayWindow()
    {
        // Build ViewModel before InitializeComponent so x:Bind can resolve it.
        ViewModel = new CompanionChatViewModel();
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

        PositionNearTaskbar();

        // ── Subscribe to state changes ─────────────────────────────────────
        AppServices.CompanionSession.StateChanged += OnSessionStateChanged;

        // ── Subscribe to desktop context changes (Phase 17B) ──────────────
        AppServices.DesktopContext.ContextChanged += OnDesktopContextChanged;

        Closed += (_, _) =>
        {
            AppServices.CompanionSession.StateChanged -= OnSessionStateChanged;
            AppServices.DesktopContext.ContextChanged -= OnDesktopContextChanged;

            // If the window was destroyed by the OS (Alt+F4, task-kill, etc.) rather
            // than via CloseButton_Click, the session manager still thinks the overlay
            // is visible. Reset to Hidden so the state is consistent and MainWindow
            // can create a fresh window on the next companion toggle.
            if (AppServices.CompanionSession.CurrentState != CompanionOverlayState.Hidden)
                AppServices.CompanionSession.Hide();
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
        }
        catch
        {
            // Position fails silently if display info is unavailable.
        }
    }

    // ── Initial window position ────────────────────────────────────────────────

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

    // ── Quick action click (DataTemplate command bridge) ─────────────────────
    //    x:Bind cannot resolve ViewModel commands from inside a DataTemplate
    //    whose DataContext is the item. Tag carries the QuickAction reference.

    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: QuickAction qa })
            ViewModel.ExecuteQuickActionCommand.Execute(qa);
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

    // ── Native window helper ───────────────────────────────────────────────────

    private AppWindow GetAppWindowForCurrentWindow()
    {
        var hWnd  = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var winId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        return AppWindow.GetFromWindowId(winId);
    }
}
