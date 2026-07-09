using System.ComponentModel;
using Lucid.Helpers;
using Lucid.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Lucid.Views;

/// <summary>
/// Dashboard page — system overview with health score, live metrics,
/// trends, and quick actions.
///
/// Code-behind responsibilities (intentionally thin):
///
///   ViewModel   — instantiated here; exposed as a public property so
///                 the XAML can reach it via x:Bind.
///
///   Entrance    — AnimateProgressBar runs once on Page_Loaded, animating
///                 each bar from 0 → its first real value as telemetry arrives.
///
///   Live bars   — AnimationHelper.AnimateProgressBar uses FillBehavior.HoldEnd,
///                 so x:Bind cannot update the bars after the Storyboard runs.
///                 OnViewModelPropertyChanged listens for CpuPercent / RamPercent /
///                 GpuPercent / DiskPercent changes and calls UpdateProgressBar,
///                 which starts a new Storyboard from the current held value to
///                 the new target — giving smooth animated transitions on every
///                 telemetry tick.
///
///   Cleanup     — Page.Unloaded stops the TelemetryPoller and removes the
///                 PropertyChanged subscription to prevent leaks on navigation.
///
///   Hover       — AnimationHelper delegates scale + border effects.
/// </summary>
public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = new DashboardViewModel(
        AppServices.Telemetry,
        AppServices.Intelligence,
        AppServices.EarlyWarning,
        AppServices.AnomalyDetection,
        AppServices.DriftDetection,
        AppServices.HealthTrajectory,
        AppServices.HistoricalAnalytics,
        AppServices.InterventionMemory,
        AppServices.PersonalizationEngine,
        AppServices.UserBehaviorClassifier,
        AppServices.Watchtower,
        AppServices.PersonalityClassifier,
        AppServices.AlertFatigueManager,
        AppServices.RecommendationExplanation,
        AppServices.LearningService);

    public DashboardPage()
    {
        InitializeComponent();

        // Subscribe once here so the handler is registered before Page_Loaded fires
        // and is always paired with the Unloaded removal below.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.Cleanup();
        };
    }

    // ── Page lifecycle ────────────────────────────────────────────────────

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Entrance animation: bars animate from 0 to the initial ViewModel value.
        // On first load this is 0 → 0 (invisible no-op), because telemetry hasn't
        // arrived yet. The first real values flow through OnViewModelPropertyChanged
        // about one second later, triggering the visible fill animation.
        AnimationHelper.AnimateProgressBar(CpuBar,  ViewModel.CpuPercent);
        AnimationHelper.AnimateProgressBar(RamBar,  ViewModel.RamPercent);
        AnimationHelper.AnimateProgressBar(GpuBar,  ViewModel.GpuPercent);
        AnimationHelper.AnimateProgressBar(DiskBar, ViewModel.DiskPercent);
    }

    // ── Live telemetry → progress bars ────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Respond only to the raw percentage properties that drive the bars.
        // Text properties (CpuDisplay, RamDetail, etc.) update automatically via x:Bind.
        switch (e.PropertyName)
        {
            case nameof(DashboardViewModel.CpuPercent):
                AnimationHelper.UpdateProgressBar(CpuBar,  ViewModel.CpuPercent);
                break;
            case nameof(DashboardViewModel.RamPercent):
                AnimationHelper.UpdateProgressBar(RamBar,  ViewModel.RamPercent);
                break;
            case nameof(DashboardViewModel.GpuPercent):
                AnimationHelper.UpdateProgressBar(GpuBar,  ViewModel.GpuPercent);
                break;
            case nameof(DashboardViewModel.DiskPercent):
                AnimationHelper.UpdateProgressBar(DiskBar, ViewModel.DiskPercent);
                break;
        }
    }

    // ── Companion shortcut ────────────────────────────────────────────────────

    private void OpenCompanionButton_Click(object sender, RoutedEventArgs e)
        => AppServices.CompanionSession.Expand();

    // ── Health card → score breakdown ─────────────────────────────────────────

    private void HealthCard_Tapped(object sender, TappedRoutedEventArgs e)
        => Frame?.Navigate(typeof(HealthBreakdownPage),
               null, new Microsoft.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo());

    // ── Hover effects ─────────────────────────────────────────────────────

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        => AnimationHelper.PointerEntered(sender);

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        => AnimationHelper.PointerExited(sender);
}
