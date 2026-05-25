using Lucid.Services.Companion;
using Lucid.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Lucid.Views;

/// <summary>
/// Companion Overlay Window — a floating, always-on-top panel that surfaces
/// operational intelligence without forcing the user to switch applications.
///
/// Window management:
///   • Always-on-top via OverlappedPresenter.IsAlwaysOnTop
///   • Frameless (no OS title bar) — content extends into the title area
///   • Drag is handled by designating header elements as the title bar region
///   • Resize is driven by ViewModel.ModeChanged events
///
/// Lifetime:
///   Created from MainWindow on first companion toggle.
///   Hidden/shown via Visibility rather than Close/new-instance.
///   Closed event unsubscribes ViewModel and releases resources.
///
/// Threading:
///   All UI manipulation runs on the UI thread.
///   SendMessageCommand / ExecuteQuickActionCommand dispatch to the thread pool
///   internally and marshal results back before updating the UI.
/// </summary>
public sealed partial class CompanionWindow : Window
{
    public CompanionViewModel ViewModel { get; }

    private readonly AppWindow           _appWindow;
    private readonly OverlappedPresenter _presenter;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcher;

    // ── Window sizing constants ────────────────────────────────────────────────

    private static readonly SizeInt32 BubbleSize   = new(64,  64);
    private static readonly SizeInt32 CompactSize  = new(320, 60);
    private static readonly SizeInt32 ExpandedSize = new(360, 560);

    public CompanionWindow()
    {
        // Build ViewModel before InitializeComponent so x:Bind can resolve it
        ViewModel = new CompanionViewModel(
            AppServices.CompanionSession,
            AppServices.ConversationEngine,
            AppServices.Narrative);

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

        // Remove the OS title bar — content fills the full client area
        ExtendsContentIntoTitleBar = true;

        // ── Initial size and position ──────────────────────────────────────
        _appWindow.Resize(CompactSize);
        PositionNearTaskbar();

        // ── Subscribe to mode changes ──────────────────────────────────────
        ViewModel.ModeChanged += OnModeChanged;

        // ── Designate drag handles for current layout ──────────────────────
        // SetTitleBar on the Compact drag handle initially.
        // We re-designate on each mode switch in OnModeChanged.
        Activated += CompanionWindow_Activated;

        Closed += (_, _) =>
        {
            ViewModel.ModeChanged -= OnModeChanged;
            ViewModel.Dispose();
        };
    }

    // ── Mode-driven resize ─────────────────────────────────────────────────────

    private void OnModeChanged(object? sender, CompanionMode newMode)
    {
        if (!_uiDispatcher.HasThreadAccess)
        {
            _uiDispatcher.TryEnqueue(() => OnModeChanged(sender, newMode));
            return;
        }

        var size = newMode switch
        {
            CompanionMode.Bubble   => BubbleSize,
            CompanionMode.Compact  => CompactSize,
            _                      => ExpandedSize,
        };

        _appWindow.Resize(size);
        UpdateDragHandle(newMode);

        // Auto-scroll to latest message when expanding
        if (newMode == CompanionMode.Expanded)
            ScrollToLatestMessage();
    }

    private void UpdateDragHandle(CompanionMode mode)
    {
        // SetTitleBar designates an element as the OS-managed drag region.
        // Buttons placed OUTSIDE this element still receive pointer events normally.
        // The designated element itself loses pointer events (OS handles the drag).
        UIElement? handle = mode switch
        {
            CompanionMode.Compact  => CompactDragHandle,
            CompanionMode.Expanded => ExpandedDragHandle,
            _                      => null,
        };

        SetTitleBar(handle);
    }

    // ── Window positioning ─────────────────────────────────────────────────────

    private void PositionNearTaskbar()
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(
                _appWindow.Id, DisplayAreaFallback.Nearest);
            var work    = displayArea.WorkArea;
            var winSize = _appWindow.Size;

            // Bottom-right, 16px from edge
            var x = work.X + work.Width  - winSize.Width  - 16;
            var y = work.Y + work.Height - winSize.Height - 16;

            _appWindow.Move(new PointInt32(x, y));
        }
        catch
        {
            // Position fails silently if display info is unavailable at window creation
        }
    }

    // ── First activation: wire up initial drag handle ──────────────────────────

    private void CompanionWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= CompanionWindow_Activated;
        UpdateDragHandle(ViewModel.Mode);
    }

    // ── Quick action click (DataTemplate command bridge) ──────────────────────

    private async void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: QuickAction qa })
            await ViewModel.ExecuteQuickActionCommand.ExecuteAsync(qa);
    }

    // ── Input box Enter key ────────────────────────────────────────────────────

    private async void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && !e.KeyStatus.IsMenuKeyDown)
        {
            e.Handled = true;
            await ViewModel.SendMessageCommand.ExecuteAsync(null);
            ScrollToLatestMessage();
        }
    }

    // ── Scroll to latest message ───────────────────────────────────────────────

    private void ScrollToLatestMessage()
    {
        // Deferred scroll so the ItemsRepeater finishes layout first.
        // ChangeView is the WinUI 3 way to scroll programmatically.
        _ = _uiDispatcher.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => MessageScroller.ChangeView(null, double.MaxValue, null, true));
    }

    // ── Native window helpers ──────────────────────────────────────────────────

    private AppWindow GetAppWindowForCurrentWindow()
    {
        var hWnd    = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var winId   = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        return AppWindow.GetFromWindowId(winId);
    }
}
