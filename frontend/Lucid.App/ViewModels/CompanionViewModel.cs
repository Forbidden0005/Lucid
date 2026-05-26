using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Companion;
using Lucid.Services.Narrative;

namespace Lucid.ViewModels;

/// <summary>
/// ViewModel for the Companion Overlay Window.
///
/// Manages:
///   • Mode transitions (Bubble ↔ Compact ↔ Expanded)
///   • Conversation message list (feeds from ICompanionSessionManager)
///   • Input text and send command
///   • Quick-action chip invocations
///   • Live status line from the narrative engine
///   • Window resize triggers via ModeChanged event
///
/// Architecture:
///   Fully MVVM — no UI logic here.
///   The CompanionWindow code-behind subscribes to ModeChanged and resizes
///   the AppWindow whenever the Mode property changes.
///
/// Threading:
///   All [ObservableProperty] writes and event fires must occur on the UI thread.
///   Conversation engine processing runs on the thread pool; results are
///   dispatched back via DispatcherQueue from the window code-behind.
/// </summary>
public sealed partial class CompanionViewModel : ObservableObject, IDisposable
{
    // ── Services ──────────────────────────────────────────────────────────────

    private readonly ICompanionSessionManager        _session;
    private readonly IOperationalConversationEngine  _engine;
    private readonly IOperationalNarrativeEngine     _narrative;

    // ── Mode ──────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BubbleVisibility))]
    [NotifyPropertyChangedFor(nameof(CompactVisibility))]
    [NotifyPropertyChangedFor(nameof(ExpandedVisibility))]
    [NotifyPropertyChangedFor(nameof(ModeToggleGlyph))]
    [NotifyPropertyChangedFor(nameof(ModeToggleTooltip))]
    private CompanionMode _mode = CompanionMode.Compact;

    /// <summary>
    /// Raised when Mode changes so the Window code-behind can resize the AppWindow.
    /// Fires AFTER the Mode property is updated.
    /// </summary>
    public event EventHandler<CompanionMode>? ModeChanged;

    // ── Conversation state ─────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<CompanionMessage> _messages  = [];
    [ObservableProperty] private string                                  _inputText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoMessagesVisibility))] // NoMessagesVisibility = !HasMessages && !IsProcessing
    [NotifyPropertyChangedFor(nameof(ProcessingVisibility))]
    private bool _isProcessing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoMessagesVisibility))] // NoMessagesVisibility = !HasMessages && !IsProcessing
    private bool _hasMessages;

    // ── Status line ───────────────────────────────────────────────────────────

    [ObservableProperty] private string _statusHeadline = "Initializing…";
    [ObservableProperty] private string _statusColor    = "#6B7280";

    // ── Quick actions ─────────────────────────────────────────────────────────

    public IReadOnlyList<QuickAction> QuickActions { get; } =
    [
        new QuickAction
        {
            Id          = "qa.performance",
            Label       = "Why is it slow?",
            Glyph       = "",
            Description = "Analyze active performance findings",
            Query       = "Why is my PC slow?",
        },
        new QuickAction
        {
            Id          = "qa.memory",
            Label       = "RAM usage",
            Glyph       = "",
            Description = "Review memory pressure findings",
            Query       = "What's using my RAM?",
        },
        new QuickAction
        {
            Id          = "qa.investigate",
            Label       = "Investigate",
            Glyph       = "",
            Description = "Run root cause analysis",
            Query       = "Investigate root causes",
        },
        new QuickAction
        {
            Id          = "qa.timeline",
            Label       = "What changed?",
            Glyph       = "",
            Description = "Show recent system changes",
            Query       = "What changed today?",
        },
    ];

    // ── Derived display properties ─────────────────────────────────────────────

    public string BubbleVisibility   => Mode == CompanionMode.Bubble   ? "Visible" : "Collapsed";
    public string CompactVisibility  => Mode == CompanionMode.Compact  ? "Visible" : "Collapsed";
    public string ExpandedVisibility => Mode == CompanionMode.Expanded ? "Visible" : "Collapsed";
    public string ProcessingVisibility  => IsProcessing ? "Visible" : "Collapsed";
    public string NoMessagesVisibility  => !HasMessages && !IsProcessing ? "Visible" : "Collapsed";

    public string ModeToggleGlyph   => Mode == CompanionMode.Expanded ? "" : ""; // ↙ collapse / ↗ expand
    public string ModeToggleTooltip => Mode == CompanionMode.Expanded ? "Compact view" : "Expand";

    // ── Constructor ───────────────────────────────────────────────────────────

    public CompanionViewModel(
        ICompanionSessionManager       session,
        IOperationalConversationEngine engine,
        IOperationalNarrativeEngine    narrative)
    {
        _session   = session;
        _engine    = engine;
        _narrative = narrative;

        // Seed welcome message
        var welcome = session.BuildWelcomeMessage();
        session.AddSystemMessage(welcome.Text, welcome.Category);
        RefreshMessages();

        // Subscribe to new messages from the session manager
        session.MessageAdded += OnMessageAdded;

        // Subscribe to narrative updates for the status line
        narrative.NarrativeUpdated += OnNarrativeUpdated;

        // Seed status line from current narrative if available
        if (narrative.CurrentNarrative is { } current)
            ApplyNarrative(current);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var query = InputText.Trim();
        if (string.IsNullOrEmpty(query) || IsProcessing) return;

        InputText = string.Empty;
        _session.AddUserMessage(query);

        IsProcessing = true;

        try
        {
            // ConfigureAwait(true): continuation must run on the UI thread because
            // _session.AddSystemMessage fires MessageAdded → Messages.Add() on an
            // ObservableCollection, and IsProcessing writes an [ObservableProperty].
            // WinUI x:Bind bindings crash when PropertyChanged fires off-thread.
            var response = await _engine.ProcessQueryAsync(query).ConfigureAwait(true);
            _session.AddSystemMessage(response.Text, response.Category, response.ActionId);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task ExecuteQuickActionAsync(QuickAction? action)
    {
        if (action is null || IsProcessing) return;

        // Expand to show the response
        if (Mode != CompanionMode.Expanded)
            SetMode(CompanionMode.Expanded);

        _session.AddUserMessage(action.Query);
        IsProcessing = true;

        try
        {
            // ConfigureAwait(true): same reason as SendMessageAsync above.
            var response = await _engine.ProcessQueryAsync(action.Query).ConfigureAwait(true);
            _session.AddSystemMessage(response.Text, response.Category, response.ActionId);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        var next = Mode switch
        {
            CompanionMode.Bubble   => CompanionMode.Compact,
            CompanionMode.Compact  => CompanionMode.Expanded,
            CompanionMode.Expanded => CompanionMode.Compact,
            _                      => CompanionMode.Compact,
        };
        SetMode(next);
    }

    [RelayCommand]
    private void MinimizeToBubble() => SetMode(CompanionMode.Bubble);

    [RelayCommand]
    private void ExpandFromBubble() => SetMode(CompanionMode.Compact);

    [RelayCommand]
    private void ClearConversation()
    {
        _session.ClearSession();
        var welcome = _session.BuildWelcomeMessage();
        _session.AddSystemMessage(welcome.Text, welcome.Category);
        RefreshMessages();
    }

    // ── Mode management ────────────────────────────────────────────────────────

    private void SetMode(CompanionMode newMode)
    {
        if (Mode == newMode) return;
        Mode = newMode;
        ModeChanged?.Invoke(this, newMode);
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnMessageAdded(object? sender, CompanionMessage msg)
    {
        Messages.Add(msg);
        HasMessages = Messages.Count > 0;
    }

    private void OnNarrativeUpdated(object? sender, OperationalNarrative narrative)
        => ApplyNarrative(narrative);

    private void ApplyNarrative(OperationalNarrative narrative)
    {
        StatusHeadline = narrative.Headline;
        StatusColor    = narrative.HealthState switch
        {
            SystemHealthState.Critical   => "#F87171",
            SystemHealthState.Degraded   => "#FBBF24",
            SystemHealthState.Monitoring => "#60A5FA",
            _                            => "#34D399",
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void RefreshMessages()
    {
        Messages.Clear();
        foreach (var msg in _session.Messages)
            Messages.Add(msg);
        HasMessages = Messages.Count > 0;
    }

    // ── Dispose ────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _session.MessageAdded      -= OnMessageAdded;
        _narrative.NarrativeUpdated -= OnNarrativeUpdated;
    }
}
