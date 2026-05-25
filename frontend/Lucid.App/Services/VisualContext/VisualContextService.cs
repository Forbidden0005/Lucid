using System.Runtime.InteropServices;
using Lucid.Services.Timeline;
using Microsoft.UI.Dispatching;

namespace Lucid.Services.VisualContext;

/// <summary>
/// Top-level orchestrator for consent-gated semantic desktop understanding.
///
/// Pipeline (per analysis request):
///   1. ConsentBoundScreenAnalysis.RequestConsentAsync() — user must approve
///   2. WindowSemanticAnalyzer.AnalyzeWindow() — classify the active window
///   3. ExplorerVisualInterpreter (if File Explorer) / SettingsPageInterpreter (if Settings)
///   4. VisualWorkflowLocator.LocateElements() — find targeted UI elements
///   5. ScreenCaptureCoordinator.CaptureWindowAsync() — pixel capture (if mode ≥ AnalyzeCurrentWindow)
///   6. ScreenAnalysisResult assembled and returned
///
/// GuideOnly mode skips steps 4–5, runs only semantic analysis.
///
/// Threading:
///   AnalyzeCurrentWindowAsync / GuideAnalysisAsync run on the thread pool.
///   ContextChanged is marshalled to the UI thread before raising.
///
/// ABSOLUTE PRIVACY RULES (enforced here, not just documented):
///   • NEVER calls ConsentBoundScreenAnalysis with Disabled mode.
///   • NEVER bypasses consent for ANY analysis mode.
///   • NEVER retains CapturedFrame past its ExpiresAt timestamp.
///   • NEVER uploads pixels, window contents, or any analysis result.
///   • Continuous analysis is NOT available — all calls are one-shot.
/// </summary>
public sealed class VisualContextService : IVisualContextService, IDisposable
{
    private readonly ConsentBoundScreenAnalysis  _consent;
    private readonly ScreenCaptureCoordinator    _capture;
    private readonly WindowSemanticAnalyzer      _analyzer;
    private readonly ExplorerVisualInterpreter   _explorer;
    private readonly SettingsPageInterpreter     _settings;
    private readonly VisualWorkflowLocator       _locator;
    private readonly ITimelineAggregationService _timeline;
    private readonly DispatcherQueue             _uiDispatcher;

    private SemanticWindowContext? _lastKnownContext;
    private volatile bool          _isAnalyzing;
    private readonly object        _lock = new();

    // ── Win32: foreground window ──────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public VisualContextService(
        ConsentBoundScreenAnalysis  consent,
        ScreenCaptureCoordinator    capture,
        WindowSemanticAnalyzer      analyzer,
        ExplorerVisualInterpreter   explorer,
        SettingsPageInterpreter     settings,
        VisualWorkflowLocator       locator,
        ITimelineAggregationService timeline,
        DispatcherQueue             uiDispatcher)
    {
        _consent      = consent;
        _capture      = capture;
        _analyzer     = analyzer;
        _explorer     = explorer;
        _settings     = settings;
        _locator      = locator;
        _timeline     = timeline;
        _uiDispatcher = uiDispatcher;
    }

    // ── IVisualContextService ─────────────────────────────────────────────────

    public event EventHandler<VisualContextChangedEventArgs>? ContextChanged;

    public SemanticWindowContext? LastKnownContext
    {
        get { lock (_lock) { return _lastKnownContext; } }
    }

    public bool IsAnalyzing => _isAnalyzing;

    // ── AnalyzeCurrentWindowAsync ─────────────────────────────────────────────

    public async Task<ScreenAnalysisResult> AnalyzeCurrentWindowAsync(
        string            reason,
        string            requestedBy  = "user",
        CancellationToken ct           = default)
    {
        return await RunAnalysisAsync(
            VisualAnalysisMode.AnalyzeCurrentWindow,
            reason, requestedBy, capturePixels: true, ct).ConfigureAwait(false);
    }

    // ── GuideAnalysisAsync ────────────────────────────────────────────────────

    public async Task<ScreenAnalysisResult> GuideAnalysisAsync(
        string            reason,
        string            requestedBy  = "companion",
        CancellationToken ct           = default)
    {
        return await RunAnalysisAsync(
            VisualAnalysisMode.GuideOnly,
            reason, requestedBy, capturePixels: false, ct).ConfigureAwait(false);
    }

    // ── LocateElementsAsync ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<VisualWorkflowTarget>> LocateElementsAsync(
        string            query,
        CancellationToken ct = default)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return [];

        return await Task.Run(
            () => _locator.LocateElements(hwnd, query), ct).ConfigureAwait(false);
    }

    // ── DescribeActiveWindow ──────────────────────────────────────────────────

    public string DescribeActiveWindow()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "No active window detected.";

            var ctx = _analyzer.AnalyzeWindow(hwnd);
            return ctx.ContextDescription ?? ctx.WindowTitle;
        }
        catch { return "Unable to read active window."; }
    }

    // ── RunGuidedWorkflowAsync ────────────────────────────────────────────────

    public async Task RunGuidedWorkflowAsync(
        IReadOnlyList<GuidedVisualStep> steps,
        string                         workflowTitle,
        CancellationToken              ct = default)
    {
        if (steps.Count == 0) return;

        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = TimelineEventType.VisualWorkflowStarted,
            OccurredAt = DateTimeOffset.Now,
            Title      = $"Visual workflow started: {workflowTitle}",
            Detail     = $"{steps.Count} guided steps",
            Severity   = TimelineEventSeverity.Info,
        });

        // The actual overlay management is handled by the caller (CompanionOverlayWindow
        // or a dedicated overlay host). VisualContextService only records timeline events
        // and drives the consent / analysis pipeline between steps.
        for (int i = 0; i < steps.Count && !ct.IsCancellationRequested; i++)
        {
            var step = steps[i];

            _timeline.AddExternalEvent(new TimelineEvent
            {
                Id         = TimelineEvent.NewId(),
                Type       = TimelineEventType.VisualTargetHighlighted,
                OccurredAt = DateTimeOffset.Now,
                Title      = $"Step {i + 1}/{steps.Count}: {step.StepTitle}",
                Detail     = step.Instruction,
                Severity   = TimelineEventSeverity.Info,
            });

            // If the step has a target rect, wait a moment to let the overlay render
            if (!step.TargetRect.IsEmpty)
                await Task.Delay(200, ct).ConfigureAwait(false);
        }

        _timeline.AddExternalEvent(new TimelineEvent
        {
            Id         = TimelineEvent.NewId(),
            Type       = TimelineEventType.GuidedVisualStepCompleted,
            OccurredAt = DateTimeOffset.Now,
            Title      = $"Visual workflow completed: {workflowTitle}",
            Severity   = TimelineEventSeverity.Good,
        });
    }

    // ── Core analysis pipeline ────────────────────────────────────────────────

    private async Task<ScreenAnalysisResult> RunAnalysisAsync(
        VisualAnalysisMode mode,
        string             reason,
        string             requestedBy,
        bool               capturePixels,
        CancellationToken  ct)
    {
        // Guard: only one analysis at a time
        lock (_lock)
        {
            if (_isAnalyzing) return ScreenAnalysisResult.Denied;
            _isAnalyzing = true;
        }

        try
        {
            // Step 1: Get consent
            var approved = await _consent.RequestConsentAsync(mode, reason, requestedBy, ct)
                                          .ConfigureAwait(false);
            if (!approved) return ScreenAnalysisResult.Denied;

            // Step 2: Semantic analysis (thread-pool safe)
            var hwnd    = GetForegroundWindow();
            var context = await Task.Run(
                () => _analyzer.AnalyzeWindow(hwnd), ct).ConfigureAwait(false);

            lock (_lock) { _lastKnownContext = context; }

            // Step 3: Domain-specific enrichment
            ExplorerContext?     explorerCtx = null;
            SettingsPageContext? settingsCtx = null;

            if (context.WindowType == SemanticWindowType.FileExplorer)
            {
                explorerCtx = await Task.Run(
                    () => _explorer.Interpret(hwnd, context.WindowTitle), ct)
                    .ConfigureAwait(false);
            }
            else if (context.WindowType is SemanticWindowType.WindowsSettings
                                        or SemanticWindowType.StartupSettings
                                        or SemanticWindowType.StorageSettings
                                        or SemanticWindowType.PrivacySettings
                                        or SemanticWindowType.NetworkSettings
                                        or SemanticWindowType.WindowsUpdate
                                        or SemanticWindowType.SecuritySettings)
            {
                settingsCtx = await Task.Run(
                    () => _settings.Interpret(context.WindowTitle), ct)
                    .ConfigureAwait(false);
            }

            // Step 4: Element location (GuideOnly + above)
            IReadOnlyList<VisualWorkflowTarget> targets = [];
            if (mode != VisualAnalysisMode.GuideOnly)
            {
                targets = await Task.Run(
                    () => _locator.LocateElements(hwnd,
                        BuildDefaultQuery(context.WindowType)), ct)
                    .ConfigureAwait(false);
            }

            // Step 5: Pixel capture (AnalyzeCurrentWindow and above only)
            CapturedFrame? frame = null;
            if (capturePixels && mode >= VisualAnalysisMode.AnalyzeCurrentWindow)
            {
                frame = await _capture.CaptureWindowAsync(hwnd, context.WindowTitle, ct)
                                       .ConfigureAwait(false);
            }

            // Assemble result
            var summary = BuildSummary(context, explorerCtx, settingsCtx);
            var result  = new ScreenAnalysisResult
            {
                Approved        = true,
                WindowContext   = context,
                ExplorerContext = explorerCtx,
                SettingsContext = settingsCtx,
                Targets         = targets,
                Frame           = frame,
                AnalysisSummary = summary,
                ExpiresAt       = DateTimeOffset.Now.AddMinutes(5),
            };

            // Fire ContextChanged on UI thread
            RaiseContextChanged(context, explorerCtx, settingsCtx);

            return result;
        }
        catch (OperationCanceledException)
        {
            return ScreenAnalysisResult.Denied;
        }
        catch
        {
            return ScreenAnalysisResult.Denied;
        }
        finally
        {
            lock (_lock) { _isAnalyzing = false; }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildDefaultQuery(SemanticWindowType type) => type switch
    {
        SemanticWindowType.StartupSettings => "startup toggle",
        SemanticWindowType.StorageSettings => "cleanup button",
        SemanticWindowType.DeviceManager   => "network adapter",
        SemanticWindowType.WindowsUpdate   => "check for updates",
        _                                  => "ok button",
    };

    private static string BuildSummary(
        SemanticWindowContext context,
        ExplorerContext?      explorer,
        SettingsPageContext?  settings)
    {
        if (explorer is not null)
            return explorer.Description ?? context.ContextDescription ?? context.WindowTitle;

        if (settings is not null)
            return settings.Description ?? context.ContextDescription ?? context.WindowTitle;

        return context.ContextDescription ?? context.WindowTitle;
    }

    private void RaiseContextChanged(
        SemanticWindowContext context,
        ExplorerContext?      explorer,
        SettingsPageContext?  settings)
    {
        var args = new VisualContextChangedEventArgs
        {
            Context  = context,
            Explorer = explorer,
            Settings = settings,
        };

        if (_uiDispatcher.HasThreadAccess)
            ContextChanged?.Invoke(this, args);
        else
            _uiDispatcher.TryEnqueue(() => ContextChanged?.Invoke(this, args));
    }

    public void Dispose()
    {
        // No managed resources to dispose currently; placeholder for future use.
    }
}
