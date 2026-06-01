using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Execution;
using Lucid.Services.History;
using Lucid.Services.Security;
using Lucid.Services.Startup;
using Lucid.Services.Timeline;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ── Finding row ViewModel ─────────────────────────────────────────────────────

public sealed class SecurityFindingViewModel
{
    public SecurityFinding Finding     { get; }
    public string  Title               => Finding.Title;
    public string  Detail              => Finding.Detail;
    public string  Explanation         => Finding.Explanation;
    public string  Severity            => Finding.SeverityLabel;
    public string  Confidence          => Finding.ConfidenceLabel;
    public string  TrustLabel          => Finding.TrustLabel;
    public string  FilePath            => Finding.FilePath;
    public bool    HasFilePath         => Finding.HasFilePath;
    public bool    IsHighSeverity      => Finding.Severity >= FindingSeverity.High;
    public bool    IsModerate          => Finding.Severity == FindingSeverity.Moderate;

    public Visibility HighBadgeVisibility =>
        IsHighSeverity ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ModerateBadgeVisibility =>
        IsModerate ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FilePathVisibility =>
        HasFilePath ? Visibility.Visible : Visibility.Collapsed;

    public SecurityFindingViewModel(SecurityFinding f) => Finding = f;
}

// ── Startup trust row ViewModel ───────────────────────────────────────────────

public sealed class StartupTrustViewModel
{
    public StartupTrustEntry Entry      { get; }
    public string  Name                 => Entry.Name;
    public string  Publisher            => string.IsNullOrEmpty(Entry.Publisher) ? "No signature" : Entry.Publisher;
    public string  TrustLabel           => Entry.TrustLabel;
    public string  Location             => Entry.Location;
    public bool    IsSigned             => Entry.IsSigned;
    public bool    IsEnabled            => Entry.IsEnabled;
    public bool    HasRisk              => Entry.HasRisk;

    public Visibility RiskBadgeVisibility =>
        HasRisk ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SignedBadgeVisibility =>
        IsSigned ? Visibility.Visible : Visibility.Collapsed;

    public StartupTrustViewModel(StartupTrustEntry e) => Entry = e;
}

// ── Feature status row ────────────────────────────────────────────────────────

public sealed class SecurityFeatureViewModel
{
    public SecurityFeatureStatus Status { get; }
    public string Name                  => Status.FeatureName;
    public string StatusLabel           => Status.StatusLabel;
    public string Detail                => Status.StatusDetail;
    public bool   IsHealthy             => Status.IsEnabled && Status.IsHealthy;
    public bool   IsDisabled            => !Status.IsEnabled;

    public Visibility GoodVisibility     => IsHealthy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WarningVisibility  => !IsHealthy ? Visibility.Visible : Visibility.Collapsed;

    public SecurityFeatureViewModel(SecurityFeatureStatus s) => Status = s;
}

// ═════════════════════════════════════════════════════════════════════════════
//  SecurityViewModel
// ═════════════════════════════════════════════════════════════════════════════

public sealed partial class SecurityViewModel : ObservableObject
{
    private readonly DispatcherQueue             _dispatcher;
    private readonly ITimelineAggregationService _timeline;
    private readonly IActionExecutionEngine      _executionEngine;
    private readonly IStartupManagementService   _startupManagement;

    // ── Scan state ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(CancelButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(ProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(ResultsVisibility))]    // ResultsVisibility = HasResults && !IsScanning
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))] // EmptyStateVisibility = !HasResults && !IsScanning
    private bool _isScanning;

    [ObservableProperty] private int    _scanProgress;
    [ObservableProperty] private string _scanPhase = string.Empty;

    // ── Result state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    private bool _hasResults;

    [ObservableProperty] private int    _postureScore;
    [ObservableProperty] private string _postureLabel  = string.Empty;
    [ObservableProperty] private string _scanDuration  = string.Empty;
    [ObservableProperty] private string _statusText    = string.Empty;

    // ── Visibility ────────────────────────────────────────────────────────────

    public Visibility ScanButtonVisibility   => V(!IsScanning);
    public Visibility CancelButtonVisibility => V(IsScanning);
    public Visibility ProgressVisibility     => V(IsScanning);
    public Visibility ResultsVisibility      => V(HasResults && !IsScanning);
    public Visibility EmptyStateVisibility   => V(!HasResults && !IsScanning);
    public Visibility NoFindingsVisibility   => V(HasResults && Findings.Count == 0);
    public Visibility FindingsVisibility     => V(Findings.Count > 0);

    private static Visibility V(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<SecurityFeatureViewModel> Features  { get; } = new();
    public ObservableCollection<SecurityFindingViewModel> Findings  { get; } = new();
    public ObservableCollection<StartupTrustViewModel>    StartupEntries { get; } = new();

    // ── Posture score color ───────────────────────────────────────────────────
    public string PostureScoreBrushKey => PostureScore switch
    {
        >= 90 => "StatusGoodBrush",
        >= 75 => "StatusInfoBrush",
        >= 60 => "StatusModerateBrush",
        _     => "StatusCriticalBrush",
    };

    // ── Construction ──────────────────────────────────────────────────────────

    public SecurityViewModel(
        ITimelineAggregationService timeline,
        IActionExecutionEngine      executionEngine,
        IStartupManagementService   startupManagement)
    {
        _timeline          = timeline;
        _executionEngine   = executionEngine;
        _startupManagement = startupManagement;
        _dispatcher        = DispatcherQueue.GetForCurrentThread()
                          ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    private CancellationTokenSource? _cts;

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;

        _cts = new CancellationTokenSource();
        IsScanning = true;
        HasResults = false;
        ScanProgress = 0;
        ScanPhase    = "Preparing…";
        StatusText   = string.Empty;
        Features.Clear();
        Findings.Clear();
        StartupEntries.Clear();

        var timeline = _timeline as Services.Timeline.TimelineAggregationService;
        var svc = new SecurityIntelligenceService(_dispatcher, _startupManagement, timeline);

        svc.ScanProgressChanged += (_, pct) =>
        {
            ScanProgress = pct;
            ScanPhase = pct switch
            {
                < 20 => "Reading Windows security status…",
                < 50 => "Verifying startup signatures…",
                < 80 => "Analyzing findings…",
                _    => "Calculating posture score…",
            };
        };

        svc.ScanCompleted += OnCompleted;

        try
        {
            await svc.StartScanAsync(_cts.Token);
        }
        finally
        {
            svc.ScanCompleted -= OnCompleted;
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelScan() => _cts?.Cancel();

    [RelayCommand]
    private async Task OpenVirusTotalAsync(SecurityFindingViewModel vm)
    {
        if (vm is null) return;
        try
        {
            var log = new ActionExecutionLog();
            var context = new ActionExecutionContext
            {
                IsElevated          = false,
                ConfirmationGranted = true,
                Log                 = log,
                Parameters = new Dictionary<string, string>
                {
                    [Services.Execution.Executors.OpenVirusTotalExecutor.ParamFilePath] = vm.FilePath,
                    [Services.Execution.Executors.OpenVirusTotalExecutor.ParamFileName] = vm.Title,
                },
            };
            await _executionEngine
                .ExecuteAsync("action.security.open-virustotal", context)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open VirusTotal: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[SecurityVM] OpenVirusTotalAsync failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task OpenFileLocationAsync(SecurityFindingViewModel vm)
    {
        if (vm is null || !vm.HasFilePath) return;
        try
        {
            var log = new ActionExecutionLog();
            var context = new ActionExecutionContext
            {
                IsElevated          = false,
                ConfirmationGranted = true,
                Log                 = log,
                Parameters = new Dictionary<string, string>
                {
                    [Services.Execution.Executors.OpenProcessLocationExecutor.ParamExecutablePath] = vm.FilePath,
                },
            };
            await _executionEngine
                .ExecuteAsync("action.process.open-location", context)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open file location: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[SecurityVM] OpenFileLocationAsync failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task OpenWindowsSecurityAsync()
    {
        try
        {
            var log = new ActionExecutionLog();
            var context = new ActionExecutionContext { IsElevated = false, ConfirmationGranted = true, Log = log };
            await _executionEngine
                .ExecuteAsync("action.security.open-windows-security", context)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SecurityVM] OpenWindowsSecurityAsync failed: {ex}");
        }
    }

    // ── Scan completion ───────────────────────────────────────────────────────

    private void OnCompleted(object? sender, SecurityAnalysisResult result)
    {
        if (result.WasCancelled)
        {
            StatusText = "Scan cancelled.";
            return;
        }

        // Populate Windows security features
        Features.Clear();
        foreach (var f in result.WindowsStatus.All)
            Features.Add(new SecurityFeatureViewModel(f));

        // Populate findings
        Findings.Clear();
        foreach (var f in result.Findings)
            Findings.Add(new SecurityFindingViewModel(f));

        // Populate startup trust entries
        StartupEntries.Clear();
        foreach (var e in result.StartupEntries.OrderByDescending(e => e.TrustLevel))
            StartupEntries.Add(new StartupTrustViewModel(e));

        PostureScore  = result.PostureScore;
        PostureLabel  = result.PostureLabel;
        ScanDuration  = result.Duration.TotalSeconds < 60
            ? $"{result.Duration.TotalSeconds:F0}s"
            : $"{result.Duration.TotalMinutes:F1} min";
        StatusText    = $"Scan complete — {result.Findings.Count} finding(s) — posture: {result.PostureLabel}";
        HasResults    = true;
        ScanPhase     = "Complete";

        OnPropertyChanged(nameof(PostureScoreBrushKey));
        OnPropertyChanged(nameof(NoFindingsVisibility));
        OnPropertyChanged(nameof(FindingsVisibility));
    }

    public void Cleanup() => _cts?.Cancel();
}
