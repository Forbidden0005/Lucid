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
    public string InsightId       { get; init; } = "";
    public string Title           { get; init; } = "";
    public string SeverityLabel   { get; init; } = "";
    public string SeverityColor   { get; init; } = "#6B7280";
    public string PointsText      { get; init; } = "";
    public string OccurrenceText  { get; init; } = "";
    public string SeenText        { get; init; } = "";
}

/// <summary>
/// ViewModel for <c>HealthBreakdownPage</c>. Explains where the dashboard
/// health score comes from: the current score, a plain-English description of
/// the formula, and a per-condition list of exactly what deducted points —
/// each linkable to its insight detail (where the fix lives).
///
/// Read-only and cold-start safe: an empty contribution list renders the
/// "nothing is dragging your score down" state.
/// </summary>
public sealed partial class HealthBreakdownViewModel : ObservableObject
{
    private readonly IHistoricalAnalyticsEngine _analytics;
    private readonly DispatcherQueue            _dispatcher;

    public HealthBreakdownViewModel(IHistoricalAnalyticsEngine analytics)
    {
        _analytics  = analytics;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
                      ?? throw new InvalidOperationException(
                          "HealthBreakdownViewModel must be created on the UI thread.");
    }

    // ── Header ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreText))]
    private int _score = 100;

    public string ScoreText => $"{Score}";

    [ObservableProperty] private string _scoreLabel = "…";
    [ObservableProperty] private string _windowText = "Based on the past 7 days";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContributions))]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    [NotifyPropertyChangedFor(nameof(ListVisibility))]
    private bool _isLoaded;

    public ObservableCollection<HealthContributionRow> Contributions { get; } = [];

    public bool HasContributions => Contributions.Count > 0;

    // Visibility helpers (string form matches the app's other pages).
    public string ListVisibility  => IsLoaded && HasContributions ? "Visible" : "Collapsed";
    public string EmptyVisibility  => IsLoaded && !HasContributions ? "Visible" : "Collapsed";

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
    /// </summary>
    public async Task LoadAsync()
    {
        HealthScore? health = null;
        try
        {
            var summary = await _analytics.ComputeAsync().ConfigureAwait(true);
            health = summary.SevenDayHealth;
        }
        catch
        {
            // Best-effort: fall through to the empty/neutral state below.
        }

        Contributions.Clear();

        if (health is not null)
        {
            Score      = health.Score;
            ScoreLabel = $"System Health: {health.Label}";

            foreach (var c in health.Contributions)
                Contributions.Add(ToRow(c));
        }

        OnPropertyChanged(nameof(HasContributions));
        IsLoaded = true;
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
            SeverityLabel  = label,
            SeverityColor  = color,
            PointsText     = $"−{c.PointsDeducted}",
            OccurrenceText = c.Occurrences == 1 ? "1 occurrence" : $"{c.Occurrences} occurrences",
            SeenText       = seen,
        };
    }
}
