using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Core.Infrastructure;
using Lucid.Services.Automation;
using Lucid.Services.Companion;
using Lucid.Services.LlmChat;
using Microsoft.UI.Dispatching;

namespace Lucid.ViewModels;

/// <summary>
/// ViewModel for the Companion Overlay Window conversation area.
///
/// Powered by a local Ollama LLM — no cloud, no API keys, free forever.
/// The model and endpoint are configured in Settings → AI Assistant.
/// Each message injects live system data (telemetry, insights, processes, timeline)
/// into the LLM context so responses are grounded in this machine's actual state.
///
/// Streaming: responses arrive word-by-word. The last message in the collection
/// is updated in place as chunks arrive (ObservableCollection index replace).
///
/// Threading: all ObservableCollection mutations happen on the UI thread via
/// the captured DispatcherQueue.
/// </summary>
public sealed partial class CompanionChatViewModel : ObservableObject
{
    private const int MaxMessages = 100;

    // ── Dependencies ───────────────────────────────────────────────────────────

    private readonly ILlmChatService         _llm;
    private readonly AutomationOrchestrator? _orchestrator;
    private readonly ILucidLogger?           _logger;
    private readonly DispatcherQueue         _dispatcher;

    // ── Cancellation for in-flight stream ─────────────────────────────────────

    private CancellationTokenSource? _streamCts;

    // ── Messages ───────────────────────────────────────────────────────────────

    public ObservableCollection<CompanionMessage> Messages { get; } = [];

    // ── Quick actions ──────────────────────────────────────────────────────────

    public ObservableCollection<QuickAction> QuickActions { get; } = [];

    private static readonly IReadOnlyList<QuickAction> StaticQuickActions =
    [
        new QuickAction { Id = "status",    Label = "Status",    Glyph = "",
                          Description = "Overall system status",
                          Query       = "How is my PC doing right now? Give me an overview." },
        new QuickAction { Id = "slow",      Label = "Why Slow",  Glyph = "",
                          Description = "Why is my PC slow?",
                          Query       = "Why does my PC feel slow? What is causing it?" },
        new QuickAction { Id = "storage",   Label = "Storage",   Glyph = "",
                          Description = "What is using my storage?",
                          Query       = "What is using the most storage space on my machine? Break it down for me." },
        new QuickAction { Id = "memory",    Label = "Memory",    Glyph = "",
                          Description = "Memory usage breakdown",
                          Query       = "What is using the most RAM right now?" },
        new QuickAction { Id = "heat",      Label = "Temps",     Glyph = "",
                          Description = "CPU temperature status",
                          Query       = "Is my CPU running hot? What are the temperatures?" },
        new QuickAction { Id = "processes", Label = "Processes", Glyph = "",
                          Description = "Top resource consumers",
                          Query       = "Which processes are using the most resources right now?" },
    ];

    // ── Input ──────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _inputText = string.Empty;

    // ── Processing state ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(SendButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(SpinnerVisibility))]
    private bool _isProcessing;

    public bool   CanSend               => !IsProcessing;
    public string SendButtonVisibility  => IsProcessing ? "Collapsed" : "Visible";
    public string SpinnerVisibility     => IsProcessing ? "Visible"   : "Collapsed";

    // ── Desktop context banner (set by CompanionOverlayWindow from OS events) ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextBannerVisibility))]
    private string _contextBannerText = string.Empty;

    [ObservableProperty]
    private string _contextGlyph = "";

    public string ContextBannerVisibility =>
        string.IsNullOrEmpty(ContextBannerText) ? "Collapsed" : "Visible";

    // ── LLM status ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetupBannerVisibility))]
    [NotifyPropertyChangedFor(nameof(SetupBannerText))]
    [NotifyPropertyChangedFor(nameof(SetupBannerCheckingVisibility))]
    [NotifyPropertyChangedFor(nameof(SetupBannerRetryVisibility))]
    private LlmStatus _llmStatus = LlmStatus.Unknown;

    public string SetupBannerVisibility => LlmStatus == LlmStatus.Ready ? "Collapsed" : "Visible";

    /// <summary>Shown while status check is in flight (spinner).</summary>
    public string SetupBannerCheckingVisibility =>
        LlmStatus == LlmStatus.Unknown ? "Visible" : "Collapsed";

    /// <summary>Shown when status is known and failed — allows user to recheck.</summary>
    public string SetupBannerRetryVisibility =>
        LlmStatus is LlmStatus.OllamaNotAvailable or LlmStatus.ModelNotPulled
            ? "Visible" : "Collapsed";

    public string SetupBannerText => LlmStatus switch
    {
        LlmStatus.OllamaNotAvailable =>
            "Ollama not running — run setup.bat to install it.",
        LlmStatus.ModelNotPulled =>
            $"Model not downloaded — run: ollama pull {_llm.ModelName}",
        LlmStatus.Unknown =>
            "Checking AI…",
        _ => string.Empty,
    };

    // ── Overlay state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BubbleVisibility))]
    [NotifyPropertyChangedFor(nameof(ExpandedVisibility))]
    private CompanionOverlayState _overlayState = CompanionOverlayState.Bubble;

    public string BubbleVisibility   =>
        OverlayState == CompanionOverlayState.Bubble   ? "Visible" : "Collapsed";
    public string ExpandedVisibility =>
        OverlayState == CompanionOverlayState.Expanded ? "Visible" : "Collapsed";
    public string NoMessagesVisibility =>
        Messages.Count == 0 ? "Visible" : "Collapsed";

    // ── Constructor ────────────────────────────────────────────────────────────

    public CompanionChatViewModel(
        ILlmChatService         llm,
        AutomationOrchestrator? orchestrator = null,
        ILucidLogger?           logger       = null)
    {
        _llm          = llm;
        _orchestrator = orchestrator;
        _logger       = logger;
        _dispatcher   = DispatcherQueue.GetForCurrentThread()
                        ?? throw new InvalidOperationException(
                            "CompanionChatViewModel must be created on the UI thread.");

        foreach (var qa in StaticQuickActions)
            QuickActions.Add(qa);

        AddWelcomeMessages();

        // Check LLM status in background — don't block the UI
        _ = CheckLlmStatusAsync();
    }

    // ── LLM status check ───────────────────────────────────────────────────────

    private async Task CheckLlmStatusAsync()
    {
        try
        {
            var status = await _llm.CheckStatusAsync().ConfigureAwait(false);
            _dispatcher.TryEnqueue(() => LlmStatus = status);
        }
        catch (Exception ex)
        {
            // Startup LLM status probe failed (Ollama not running, network error, etc.).
            // LlmStatus stays at its default Unknown value — the companion chat panel
            // shows the "not connected" banner. Not an unobserved exception.
            Lucid.Services.Diagnostics.Logging.OperationalDiagnostics.ReportFailure("Companion", "Initial LLM status check failed", ex);
        }
    }

    [RelayCommand]
    private async Task RetryLlmCheckAsync()
    {
        LlmStatus = LlmStatus.Unknown;   // show spinner while checking
        try
        {
            var status = await _llm.CheckStatusAsync().ConfigureAwait(true);
            LlmStatus = status;
        }
        catch (Exception ex)
        {
            // Treat any connection error as "Ollama not available" so the banner
            // transitions away from the infinite spinner state.
            LlmStatus = LlmStatus.OllamaNotAvailable;
            Lucid.Services.Diagnostics.Logging.OperationalDiagnostics.ReportFailure("Companion", "RetryLlmCheckAsync failed", ex);
        }
    }

    // ── Welcome messages ───────────────────────────────────────────────────────

    private void AddWelcomeMessages()
    {
        Messages.Add(MakeMessage(
            "Hi — I am Lucid. Ask me anything about your PC and I will answer " +
            "using real data from this machine.",
            CompanionMessageCategory.Welcome));

        Messages.Add(MakeMessage(
            "All analysis runs locally. No internet connection needed. " +
            "Nothing ever leaves this device.",
            CompanionMessageCategory.Answer));
    }

    // ── Send message ───────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text) || IsProcessing) return;

        // Add the user message bubble
        AddMessage(new CompanionMessage
        {
            Id        = NewId(),
            Role      = CompanionMessageRole.User,
            Text      = text,
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Answer,
        });

        InputText    = string.Empty;
        IsProcessing = true;

        // Check LLM availability first.
        // The status probe is a network call — guard it so a connection error
        // resets IsProcessing and shows feedback rather than becoming an
        // unobserved exception (this code runs before the streaming try/finally).
        if (!_llm.IsReady)
        {
            LlmStatus newStatus;
            try
            {
                newStatus = await _llm.CheckStatusAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AddMessage(MakeMessage(BuildErrorMessage(ex), CompanionMessageCategory.Error));
                IsProcessing = false;
                return;
            }
            LlmStatus = newStatus;

            if (!_llm.IsReady)
            {
                AddMessage(MakeMessage(SetupBannerText, CompanionMessageCategory.Warning));
                IsProcessing = false;
                return;
            }
        }

        // Add a placeholder message that will be updated as text streams in
        var streamingId = NewId();
        AddMessage(new CompanionMessage
        {
            Id        = streamingId,
            Role      = CompanionMessageRole.System,
            Text      = "",
            Timestamp = DateTimeOffset.Now,
            Category  = CompanionMessageCategory.Answer,
        });

        _streamCts = new CancellationTokenSource();
        var accumulated = new System.Text.StringBuilder();

        try
        {
            await _llm.StreamResponseAsync(
                text,
                onChunk: chunk =>
                {
                    accumulated.Append(chunk);
                    var current = accumulated.ToString();

                    // Dispatch to UI thread to update the streaming message in place
                    _dispatcher.TryEnqueue(() =>
                    {
                        var idx = Messages.Count - 1;
                        if (idx >= 0 && Messages[idx].Id == streamingId)
                            Messages[idx] = Messages[idx] with { Text = current };
                    });
                },
                ct: _streamCts.Token
            ).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // User cancelled — leave whatever text arrived
        }
        catch (Exception ex)
        {
            // Replace the streaming placeholder with an error message
            _dispatcher.TryEnqueue(() =>
            {
                var idx = Messages.Count - 1;
                if (idx >= 0 && Messages[idx].Id == streamingId)
                    Messages[idx] = Messages[idx] with
                    {
                        Text     = BuildErrorMessage(ex),
                        Category = CompanionMessageCategory.Error,
                    };
            });
        }
        finally
        {
            _streamCts?.Dispose();
            _streamCts   = null;
            IsProcessing = false;
        }
    }

    private static string BuildErrorMessage(Exception ex)
    {
        if (ex is HttpRequestException or System.Net.Sockets.SocketException)
            return "Could not reach Ollama. Make sure Ollama is running " +
                   "(it starts automatically with setup.bat).";

        return $"Something went wrong: {ex.Message}";
    }

    // ── Quick actions ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void ExecuteQuickAction(QuickAction action)
    {
        if (action is null) return;
        InputText = action.Query;
        _ = SendMessageAsync();
    }

    // ── Clear conversation ─────────────────────────────────────────────────────

    [RelayCommand]
    private void ClearConversation()
    {
        _streamCts?.Cancel();
        Messages.Clear();
        _llm.ClearHistory();
        AddWelcomeMessages();
        // NoMessagesVisibility is notified by AddWelcomeMessages() → AddMessage()
    }

    // ── Automation narration (called from CompanionOverlayWindow) ─────────────

    public void AddAutomationNarration(string narration, bool isWarning = false)
    {
        AddMessage(MakeMessage(narration,
            isWarning ? CompanionMessageCategory.Warning : CompanionMessageCategory.Action));
    }

    // ── Automation launch (business logic — moved out of the window code-behind) ─

    /// <summary>
    /// Plans and executes a desktop automation for the given target, streaming
    /// narration back into the chat. The window only routes the action tag
    /// here; planning, event wiring, and execution live in the ViewModel.
    /// </summary>
    public void LaunchAutomation(string target)
    {
        if (_orchestrator is null) return;

        var plan = _orchestrator.PlanLaunch(target);

        // One-shot subscriptions so a recycled window/VM never leaks handlers.
        WireOrchestratorEvents(_orchestrator);

        // Zero/low-risk plans execute immediately; risky ones go through the
        // approval card via the orchestrator's ConfirmationRequired flow.
        _ = Task.Run(async () =>
        {
            try
            {
                await _orchestrator.ExecutePlanAsync(plan).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Narration events surface user-facing errors; the exception
                // itself must still reach diagnostics rather than vanish.
                _logger?.Warning("Automation",
                    $"Companion automation plan for '{target}' failed: {ex.Message}", ex);
            }
        });
    }

    private void WireOrchestratorEvents(AutomationOrchestrator orchestrator)
    {
        // Lightweight handlers — they just add a narration message to the chat.
        EventHandler<AutomationNarrationEventArgs>? onNarration = null;
        EventHandler<AutomationPlanEventArgs>?      onComplete  = null;
        EventHandler<AutomationPlanEventArgs>?      onCancel    = null;

        onNarration = (_, args) => AddAutomationNarration(args.Message, args.IsWarning);

        onComplete = (_, _) =>
        {
            orchestrator.NarrationUpdated -= onNarration;
            orchestrator.PlanCompleted    -= onComplete;
            orchestrator.PlanCancelled    -= onCancel;
        };
        onCancel = (_, _) =>
        {
            orchestrator.NarrationUpdated -= onNarration;
            orchestrator.PlanCompleted    -= onComplete;
            orchestrator.PlanCancelled    -= onCancel;
        };

        orchestrator.NarrationUpdated += onNarration;
        orchestrator.PlanCompleted    += onComplete;
        orchestrator.PlanCancelled    += onCancel;
    }

    // ── Contextual suggestions (no-op with LLM — LLM handles context natively) ─

    public void RefreshContextualSuggestions() { /* LLM sees desktop context via system prompt */ }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void AddMessage(CompanionMessage message)
    {
        while (Messages.Count >= MaxMessages)
            Messages.RemoveAt(0);
        Messages.Add(message);
        OnPropertyChanged(nameof(NoMessagesVisibility));
    }

    private static CompanionMessage MakeMessage(string text, CompanionMessageCategory category) =>
        new()
        {
            Id        = NewId(),
            Role      = CompanionMessageRole.System,
            Text      = text,
            Timestamp = DateTimeOffset.Now,
            Category  = category,
        };

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];
}
