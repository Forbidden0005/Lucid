using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lucid.Services.Analytics;
using Microsoft.UI.Dispatching;

namespace Lucid.ViewModels;

/// <summary>
/// One row in the health-score breakdown list — a single condition that moved
/// the score, with the points it cost and enough context to act on it.
/// </summary>
public sealed class HealthContributionRow
{
    /// <summary>Stable identifier of the underlying insight, used to deep-link to its detail page.</summary>
    public string InsightId       { get; init; } = "";

    /// <summary>Human-readable condition title shown as the row heading.</summary>
    public string Title           { get; init; } = "";

    /// <summary>Severity chip text, e.g. "Warning" or "Suggestion · resolved".</summary>
    public string SeverityLabel   { get; init; } = "";

    /// <summary>Hex colour for the severity chip and points text.</summary>
    public string SeverityColor   { get; init; } = "#6B7280";

    /// <summary>Points this condition deducted, formatted for display (e.g. "−10").</summary>
    public string PointsText      { get; init; } = "";

    /// <summary>Occurrence count as display text (e.g. "3 occurrences").</summary>
    public string OccurrenceText  { get; init; } = "";

    /// <summary>When the condition was seen, as display text (single date or a range).</summary>
    public string SeenText        { get; init; } = "";

    /// <summary>True when the condition is still active (has a live fix path).</summary>
    public bool   IsActive        { get; init; }

    /// <summary>"Review &amp; fix" for active conditions, "View details" for resolved ones —
    /// resolved insights open the detail page in its historical (resolved) state with no fix action.</summary>
    public string ActionText      => IsActive ? "Review & fix" : "View details";
}

/// <summary>
/// ViewModel for <c>HealthBreakdownPage</c>. Explains where the dashboard
/// health score comes from: the current score, a plain-English description of
/// the formula, and a per-condition list of exactly what deducted points —
/// each linkable to its insight detail (where the fix lives).
///
/// Read-only and cold-start safe. Three terminal states, kept mutually
/// exclusive so a data-load failure can never masquerade as a healthy machine:
///   • loaded with contributions → the list,
///   • loaded with none          → the healthy empty state,
///   • load failed               → the error state.
/// </summary>
public sealed partial class HealthBreakdownViewModel : ObservableObject
{
    private readonly IHistoricalAnalyticsEngine _analytics;
    private readonly DispatcherQueue            _dispatcher;

    /// <summary>Creates the ViewModel. Must be constructed on the UI thread.</summary>
    /// <param name="analytics">Source of the historical health summary.</param>
    public HealthBreakdownViewModel(IHistoricalAnalyticsEngine analytics)
    {
        _analytics  = analytics;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
                      ?? throw new InvalidOperationException(
                          "HealthBreakdownViewModel must be created on the UI thread.");
    }

    // ── Header ──────────────────────────────────────────────────────────────

    /// <summary>Current 7-day health score in [0, 100]. Zero when the load failed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreText))]
    private int _score = 100;

    /// <summary>Score for the ring's centre label; "—" when the score is unavailable.</summary>
    public string ScoreText => LoadFailed ? "—" : $"{Score}";

    /// <summary>Header line, e.g. "System Health: Good" or "Score unavailable".</summary>
    [ObservableProperty] private string _scoreLabel = "…";

    /// <summary>Sub-header describing the window the score covers.</summary>
    [ObservableProperty] private string _windowText = "Based on the past 7 days";

    /// <summary>True once <see cref="LoadAsync"/> has completed (success or failure).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    [NotifyPropertyChangedFor(nameof(ListVisibility))]
    private bool _isLoaded;

    /// <summary>True when computing the breakdown failed (DB busy/unavailable).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    [NotifyPropertyChangedFor(nameof(ListVisibility))]
    [NotifyPropertyChangedFor(nameof(ErrorVisibility))]
    [NotifyPropertyChangedFor(nameof(ScoreText))]
    private bool _loadFailed;

    /// <summary>The per-condition contributions to render, most-impactful first.</summary>
    public ObservableCollection<HealthContributionRow> Contributions { get; } = [];

    /// <summary>True when there is at least one contributing condition to show.</summary>
    public bool HasContributions => Contributions.Count > 0;

    // Visibility helpers (string form matches the app's other pages).
    // A load failure must NOT masquerade as a healthy "nothing wrong" state.

    /// <summary>"Visible" when the contribution list should show; else "Collapsed".</summary>
    public string ListVisibility  => IsLoaded && !LoadFailed && HasContributions ? "Visible" : "Collapsed";

    /// <summary>"Visible" for the genuinely-healthy empty state; else "Collapsed".</summary>
    public string EmptyVisibility  => IsLoaded && !LoadFailed && !HasContributions ? "Visible" : "Collapsed";

    /// <summary>"Visible" for the load-failure state; else "Collapsed".</summary>
    public string ErrorVisibility  => LoadFailed ? "Visible" : "Collapsed";

    /// <summary>The scoring rule, in plain language — the "why" behind the number.</summary>
    public string FormulaExplanation =>
        "Your score starts at 100. Each distinct condition worth reviewing subtracts a few points — " +
        $"about {HealthScoreCalculator.PenaltyPerWarning} for a warning " +
        $"(a little more if it keeps recurring) and {HealthScoreCalculator.PenaltyPerRecommendation} " +
        "for a suggestion. A single ongoing issue only counts once, no matter how often it reappears, " +
        "so the number reflects how many things actually need attention — not how noisy they are.";

    // ── Load ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes a fresh breakdown from the historical analytics engine.
    /// Safe to call on the UI thread; the SQL work runs on the thread pool.
    /// On failure the ViewModel enters the error state rather than a
    /// neutral/perfect one.
    /// </summary>
    public async Task LoadAsync()
    {
        HealthScore? health = null;
        bool failed = false;
        try
        {
            var summary = await _analytics.ComputeAsync().ConfigureAwait(true);
            health = summary.SevenDayHealth;
        }
        catch
        {
            // A data-load failure must surface as an error, NOT as a neutral
            // 100/"nothing wrong" state — that would make a locked/unavailable
            // database look like a perfectly healthy machine on the very page
            // whose job is to explain the score.
            failed = true;
        }

        Contributions.Clear();

        if (health is not null)
        {
            Score      = health.Score;
            ScoreLabel = $"System Health: {health.Label}";

            foreach (var c in health.Contributions)
                Contributions.Add(ToRow(c));
        }
        else
        {
            // No usable data — show an unavailable score, not the default 100.
            Score      = 0;
            ScoreLabel = "Score unavailable";
        }

        OnPropertyChanged(nameof(HasContributions));
        LoadFailed = failed;
        IsLoaded   = true;
    }

    private static HealthContributionRow ToRow(HealthScoreContribution c)
    {
        var (label, color) = c.SeverityOrdinal >= 2
            ? ("Warning", "#FF6B6B")
            : ("Suggestion", "#FFB347");

        string seen = c.Occurrences <= 1
            ? $"Seen {c.LastSeen.LocalDateTime:MMM d}"
            : $"Seen {c.Occurrences}× · {c.FirstSeen.LocalDateTime:MMM d}–{c.LastSeen.LocalDateTime:MMM d}";

        return new HealthContributionRow
        {
            InsightId      = c.InsightId,
            Title          = string.IsNullOrWhiteSpace(c.Title) ? "Unnamed condition" : c.Title,
            SeverityLabel  = c.IsActive ? label : $"{label} · resolved",
            SeverityColor  = color,
            PointsText     = $"−{c.PointsDeducted}",
            OccurrenceText = c.Occurrences == 1 ? "1 occurrence" : $"{c.Occurrences} occurrences",
            SeenText       = seen,
            IsActive       = c.IsActive,
        };
    }
}
