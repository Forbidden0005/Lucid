using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Behavior;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ── Sub-ViewModels ─────────────────────────────────────────────────────────────

/// <summary>Presentation model for one row in the workload breakdown chart.</summary>
public sealed class WorkloadShareViewModel
{
    public string Label      { get; init; } = "";
    public string Percentage { get; init; } = "";
    public string Color      { get; init; } = "";

    /// <summary>Bar width as a 0–100 value for ProgressBar binding.</summary>
    public double BarValue   { get; init; }
}

/// <summary>Presentation model for a detected behavioral pattern card.</summary>
public sealed class PatternViewModel
{
    public string Label           { get; init; } = "";
    public string Description     { get; init; } = "";
    public string ObservationText { get; init; } = "";
    public string LastSeenText    { get; init; } = "";
    public string ConfidenceLabel { get; init; } = "";
    public string ConfidenceColor { get; init; } = "";
    public string WorkloadColor   { get; init; } = "";
}

/// <summary>Presentation model for one row in the recent sessions list.</summary>
public sealed class SessionSummaryViewModel
{
    public string     WorkloadLabel   { get; init; } = "";
    public string     WorkloadColor   { get; init; } = "";
    public string     TimeRangeText   { get; init; } = "";
    public string     DurationText    { get; init; } = "";
    public string     PeakCpuText     { get; init; } = "";
    public string     PeakRamText     { get; init; } = "";
    public Visibility ThermalBadge    { get; init; }
    public Visibility MemoryBadge     { get; init; }
}

// ── Main ViewModel ─────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Machine Behavior workspace.
///
/// Surfaces the behavioral identity of the machine and tracks active workloads,
/// recurring patterns, and completed sessions. Receives live workload-transition
/// events from <see cref="WorkloadProfilingService"/> and dispatches UI updates.
///
/// All observable property writes occur on the UI thread:
///   - Refresh uses await + ConfigureAwait(true) after Task.Run data read.
///   - WorkloadChanged fires on telemetry thread → we dispatch via DispatcherQueue.
/// </summary>
public sealed partial class MachineBehaviorViewModel : ObservableObject
{
    private readonly IBehavioralContextEngine _context;
    private readonly WorkloadProfilingService _profiling;

    // ── Loading ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    private bool _isLoading = true;

    // ── Machine identity ───────────────────────────────────────────────────────

    [ObservableProperty]
    private string _identityNarrative = "";

    [ObservableProperty]
    private string _dominantWorkloadLabel = "—";

    [ObservableProperty]
    private string _dominantWorkloadColor = "#6B7280";

    [ObservableProperty]
    private string _secondaryWorkloadLabel = "—";

    [ObservableProperty]
    private string _profileConfidenceLabel = "";

    [ObservableProperty]
    private string _totalSessionsText = "";

    // ── Current workload ───────────────────────────────────────────────────────

    [ObservableProperty]
    private string _currentWorkloadLabel = "—";

    [ObservableProperty]
    private string _currentWorkloadColor = "#6B7280";

    [ObservableProperty]
    private string _currentWorkloadRationale = "";

    [ObservableProperty]
    private string _currentConfidenceLabel = "";

    [ObservableProperty]
    private string _contextNarrative = "";

    // ── Section visibility ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPatternsVisibility))]
    [NotifyPropertyChangedFor(nameof(NoPatternsVisibility))]
    private bool _hasPatterns;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessionsVisibility))]
    [NotifyPropertyChangedFor(nameof(NoSessionsVisibility))]
    private bool _hasSessions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBreakdownVisibility))]
    [NotifyPropertyChangedFor(nameof(NoBreakdownVisibility))]
    private bool _hasBreakdown;

    // ── Derived Visibility ─────────────────────────────────────────────────────

    public Visibility LoadingVisibility    => IsLoading    ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContentVisibility    => !IsLoading   ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasPatternsVisibility => HasPatterns  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoPatternsVisibility  => !HasPatterns ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasSessionsVisibility => HasSessions  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoSessionsVisibility  => !HasSessions ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasBreakdownVisibility => HasBreakdown  ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoBreakdownVisibility  => !HasBreakdown ? Visibility.Visible : Visibility.Collapsed;

    // ── Collections ────────────────────────────────────────────────────────────

    public ObservableCollection<WorkloadShareViewModel>  WorkloadBreakdown  { get; } = [];
    public ObservableCollection<PatternViewModel>        DetectedPatterns   { get; } = [];
    public ObservableCollection<SessionSummaryViewModel> RecentSessions     { get; } = [];

    // ── Commands ───────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RefreshCommand { get; }

    // ── Dispatcher ────────────────────────────────────────────────────────────

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcher;

    // ── Construction ──────────────────────────────────────────────────────────

    public MachineBehaviorViewModel(
        IBehavioralContextEngine context,
        WorkloadProfilingService profiling)
    {
        _context      = context;
        _profiling    = profiling;
        _uiDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);

        _profiling.WorkloadChanged += OnWorkloadChanged;
    }

    // ── Initialization ─────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            // try/finally ensures the loading spinner always clears regardless of
            // whether RefreshAsync throws, preventing a permanently hidden page.
            IsLoading = false;
        }
    }

    // ── Refresh ────────────────────────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        try
        {
            // Fetch context on background thread (in-memory but potentially slow
            // if pattern detection runs over many sessions)
            var ctx = await Task.Run(() => _context.GetCurrentContext())
                                .ConfigureAwait(true);

            ApplyContext(ctx);
        }
        catch (Exception ex)
        {
            // Behavioral context read failed — leave whatever state was previously
            // rendered rather than crashing through async void OnNavigatedTo.
            System.Diagnostics.Debug.WriteLine($"[MachineBehaviorVM] RefreshAsync failed: {ex.Message}");
        }
    }

    // ── Apply context ──────────────────────────────────────────────────────────

    private void ApplyContext(BehavioralContext ctx)
    {
        // ── Machine identity ───────────────────────────────────────────────────
        var identity = ctx.MachineIdentity;
        IdentityNarrative      = identity.IdentityNarrative;
        DominantWorkloadLabel  = WorkloadLabel(identity.DominantWorkload);
        DominantWorkloadColor  = WorkloadColor(identity.DominantWorkload);
        SecondaryWorkloadLabel = identity.SecondaryWorkload != WorkloadType.Unknown
            ? WorkloadLabel(identity.SecondaryWorkload)
            : "—";
        ProfileConfidenceLabel = $"{identity.Confidence} confidence";
        TotalSessionsText      = identity.TotalSessionsAnalyzed == 0
            ? "No sessions recorded yet"
            : $"{identity.TotalSessionsAnalyzed} session{(identity.TotalSessionsAnalyzed == 1 ? "" : "s")} analyzed";

        // ── Current workload ───────────────────────────────────────────────────
        var cl = ctx.CurrentWorkload;
        CurrentWorkloadLabel    = WorkloadLabel(cl.PrimaryWorkload);
        CurrentWorkloadColor    = WorkloadColor(cl.PrimaryWorkload);
        CurrentWorkloadRationale = cl.Rationale;
        CurrentConfidenceLabel  = $"{cl.Confidence} confidence";
        ContextNarrative        = _context.GetContextNarrative();

        // ── Workload breakdown ─────────────────────────────────────────────────
        WorkloadBreakdown.Clear();
        foreach (var (type, _, share) in identity.WorkloadBreakdown)
        {
            WorkloadBreakdown.Add(new WorkloadShareViewModel
            {
                Label      = WorkloadLabel(type),
                Percentage = $"{share:F0}%",
                Color      = WorkloadColor(type),
                BarValue   = share,
            });
        }
        HasBreakdown = WorkloadBreakdown.Count > 0;

        // ── Behavioral patterns ────────────────────────────────────────────────
        DetectedPatterns.Clear();
        foreach (var pattern in ctx.ActivePatterns)
        {
            DetectedPatterns.Add(new PatternViewModel
            {
                Label           = pattern.Label,
                Description     = pattern.Description,
                ObservationText = $"{pattern.ObservationCount} observation{(pattern.ObservationCount == 1 ? "" : "s")}",
                LastSeenText    = FormatAgo(pattern.LastObservedAt),
                ConfidenceLabel = pattern.Confidence.ToString(),
                ConfidenceColor = ConfidenceColor(pattern.Confidence),
                WorkloadColor   = WorkloadColor(pattern.AssociatedWorkload),
            });
        }
        HasPatterns = DetectedPatterns.Count > 0;

        // ── Recent sessions ────────────────────────────────────────────────────
        RecentSessions.Clear();
        foreach (var session in ctx.RecentSessions)
        {
            RecentSessions.Add(new SessionSummaryViewModel
            {
                WorkloadLabel = WorkloadLabel(session.DominantWorkload),
                WorkloadColor = WorkloadColor(session.DominantWorkload),
                TimeRangeText = $"{session.StartedAt.LocalDateTime:h:mm tt} – {session.EndedAt.LocalDateTime:h:mm tt}",
                DurationText  = FormatDuration(session.Duration),
                PeakCpuText   = $"CPU {session.PeakCpuPct:F0}%",
                PeakRamText   = $"RAM {session.PeakRamPct:F0}%",
                ThermalBadge  = session.HadThermalStress   ? Visibility.Visible : Visibility.Collapsed,
                MemoryBadge   = session.HadMemoryPressure  ? Visibility.Visible : Visibility.Collapsed,
            });
        }
        HasSessions = RecentSessions.Count > 0;
    }

    // ── Live workload event ────────────────────────────────────────────────────

    private void OnWorkloadChanged(object? sender, WorkloadClassification e)
    {
        // WorkloadChanged fires on telemetry thread — dispatch to UI
        _uiDispatcher?.TryEnqueue(() =>
        {
            CurrentWorkloadLabel    = WorkloadLabel(e.PrimaryWorkload);
            CurrentWorkloadColor    = WorkloadColor(e.PrimaryWorkload);
            CurrentWorkloadRationale = e.Rationale;
            CurrentConfidenceLabel  = $"{e.Confidence} confidence";
            ContextNarrative        = _context.GetContextNarrative();
        });
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    public void Cleanup() => _profiling.WorkloadChanged -= OnWorkloadChanged;

    // ── Static formatting helpers ──────────────────────────────────────────────

    internal static string WorkloadLabel(WorkloadType t) => t switch
    {
        WorkloadType.Gaming              => "Gaming",
        WorkloadType.Development         => "Development",
        WorkloadType.MediaEditing        => "Media Editing",
        WorkloadType.Rendering           => "Rendering",
        WorkloadType.Streaming           => "Streaming",
        WorkloadType.BrowserResearch     => "Browser / Research",
        WorkloadType.Productivity        => "Productivity",
        WorkloadType.BackgroundProcessing => "Background Processing",
        WorkloadType.StartupHeavy        => "Startup-Heavy",
        WorkloadType.ThermalIntensive    => "Thermal-Intensive",
        WorkloadType.OvernightProcessing => "Overnight Processing",
        WorkloadType.Idle                => "Idle",
        _                               => "Unknown",
    };

    internal static string WorkloadColor(WorkloadType t) => t switch
    {
        WorkloadType.Gaming              => "#A855F7",  // purple
        WorkloadType.Development         => "#3B82F6",  // blue
        WorkloadType.MediaEditing        => "#F59E0B",  // amber
        WorkloadType.Rendering           => "#EF4444",  // red
        WorkloadType.Streaming           => "#10B981",  // emerald
        WorkloadType.BrowserResearch     => "#6366F1",  // indigo
        WorkloadType.Productivity        => "#22C55E",  // green
        WorkloadType.BackgroundProcessing => "#6B7280", // gray
        WorkloadType.StartupHeavy        => "#F97316",  // orange
        WorkloadType.ThermalIntensive    => "#EF4444",  // red
        WorkloadType.OvernightProcessing => "#0EA5E9",  // sky
        WorkloadType.Idle                => "#4B5563",  // dark gray
        _                               => "#6B7280",
    };

    private static string ConfidenceColor(BehaviorConfidence c) => c switch
    {
        BehaviorConfidence.High   => "#22C55E",
        BehaviorConfidence.Medium => "#F59E0B",
        _                         => "#6B7280",
    };

    private static string FormatAgo(DateTimeOffset at)
    {
        var ago = DateTimeOffset.Now - at;
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours   < 24) return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m";
        return $"{(int)ts.TotalHours}h {ts.Minutes}m";
    }
}
