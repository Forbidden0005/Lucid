using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Distributed;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ── Sub-ViewModels ────────────────────────────────────────────────────────────

/// <summary>Card ViewModel for one trusted linked device.</summary>
public sealed class TrustedDeviceCardViewModel
{
    public string DeviceId        { get; init; } = string.Empty;
    public string DeviceName      { get; init; } = string.Empty;
    public string RoleLabel       { get; init; } = string.Empty;
    public string RoleGlyph       { get; init; } = string.Empty;
    public string RoleColor       { get; init; } = "#6B7280";
    public string SyncStatusLabel { get; init; } = string.Empty;
    public string SyncStatusColor { get; init; } = "#6B7280";
    public string LastSyncText    { get; init; } = string.Empty;
    public string WorkloadLabel   { get; init; } = string.Empty;
    public string WorkloadColor   { get; init; } = "#6B7280";
    public string UptimeText      { get; init; } = string.Empty;
    public string CpuText         { get; init; } = string.Empty;
    public string RamText         { get; init; } = string.Empty;
    public bool   IsOnline        { get; init; }
}

/// <summary>Card ViewModel for one cross-machine insight.</summary>
public sealed class CrossInsightViewModel
{
    public string InsightId        { get; init; } = string.Empty;
    public string Title            { get; init; } = string.Empty;
    public string Explanation      { get; init; } = string.Empty;
    public string TypeLabel        { get; init; } = string.Empty;
    public string TypeColor        { get; init; } = "#6B7280";
    public string ConfidenceLabel  { get; init; } = string.Empty;
    public string ConfidenceColor  { get; init; } = "#6B7280";
    public string AffectedDevices  { get; init; } = string.Empty;
    public string TimeText         { get; init; } = string.Empty;
    public string TypeGlyph        { get; init; } = string.Empty;
}

/// <summary>Row ViewModel for a distributed timeline event.</summary>
public sealed class DistributedTimelineEventViewModel
{
    public string EventId          { get; init; } = string.Empty;
    public string SourceDeviceName { get; init; } = string.Empty;
    public string Title            { get; init; } = string.Empty;
    public string Description      { get; init; } = string.Empty;
    public string Glyph            { get; init; } = string.Empty;
    public string AccentColor      { get; init; } = "#6B7280";
    public string TimeText         { get; init; } = string.Empty;
    public bool   IsLocalDevice    { get; init; }
    public bool   IsCorrelated     { get; init; }
    public string DeviceBadge      { get; init; } = string.Empty;
    public string DeviceBadgeColor { get; init; } = "#6B7280";
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Device Intelligence page.
/// Exposes cross-machine operational awareness: trusted devices, cross-machine
/// insights, distributed timeline, ecosystem narrative, and pairing flow.
/// All data is local-only — no cloud, no telemetry upload.
/// </summary>
public sealed partial class DeviceIntelligenceViewModel : ObservableObject
{
    private readonly DeviceIdentityService      _identity;
    private readonly TrustedDeviceRegistry      _registry;
    private readonly LocalSyncCoordinator       _sync;
    private readonly DistributedTimelineAggregator _timelineAgg;
    private readonly CrossMachineAnalyticsEngine   _analytics;
    private readonly DispatcherQueue            _ui;

    // ── Observable properties ─────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    [NotifyPropertyChangedFor(nameof(DeviceListVisibility))]
    private bool _isNoDevicesLinked = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PairingPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    private bool _isPairingMode;

    // This device identity
    [ObservableProperty] private string _thisDeviceName  = string.Empty;
    [ObservableProperty] private string _thisDeviceRole  = string.Empty;
    [ObservableProperty] private string _thisDeviceGlyph = string.Empty;
    [ObservableProperty] private string _thisDeviceColor = "#6B7280";
    [ObservableProperty] private string _thisDeviceId    = string.Empty;
    [ObservableProperty] private string _thisDeviceOs    = string.Empty;

    // Pairing
    [ObservableProperty] private string _pairingCode         = string.Empty;
    [ObservableProperty] private string _pairingInstructions = string.Empty;

    // Ecosystem
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InsightsSectionVisibility))]
    private bool _hasCrossInsights;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimelineSectionVisibility))]
    private bool _hasDistributedEvents;

    [ObservableProperty] private string _ecosystemNarrative = string.Empty;
    [ObservableProperty] private string _linkedDeviceCount  = "0 linked devices";
    [ObservableProperty] private string _syncStatusText     = "Not syncing";
    [ObservableProperty] private string _pairingCode6       = "——————";

    // ── Computed Visibility props (no converters needed in XAML) ─────────────

    public Visibility LoadingVisibility    => IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PairingPanelVisibility => IsPairingMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyStateVisibility =>
        !IsNoDevicesLinked || IsPairingMode ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DeviceListVisibility =>
        IsNoDevicesLinked ? Visibility.Collapsed : Visibility.Visible;

    public Visibility InsightsSectionVisibility =>
        HasCrossInsights ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TimelineSectionVisibility =>
        HasDistributedEvents ? Visibility.Visible : Visibility.Collapsed;

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<TrustedDeviceCardViewModel>        TrustedDevices   { get; } = [];
    public ObservableCollection<CrossInsightViewModel>             CrossInsights    { get; } = [];
    public ObservableCollection<DistributedTimelineEventViewModel> DistributedEvents { get; } = [];

    // ── Construction ──────────────────────────────────────────────────────────

    public DeviceIntelligenceViewModel(
        DeviceIdentityService         identity,
        TrustedDeviceRegistry         registry,
        LocalSyncCoordinator          sync,
        DistributedTimelineAggregator timelineAgg,
        CrossMachineAnalyticsEngine   analytics)
    {
        _identity    = identity;
        _registry    = registry;
        _sync        = sync;
        _timelineAgg = timelineAgg;
        _analytics   = analytics;
        _ui          = DispatcherQueue.GetForCurrentThread();

        _sync.SnapshotReceived += OnSnapshotReceived;
    }

    // ── Initialization ────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        IsLoading = true;

        try
        {
            await Task.Run(() =>
            {
                // Identity probe (may call WMI — run off UI thread)
                var id = _identity.GetOrCreateIdentity();

                _ui.TryEnqueue(() =>
                {
                    ThisDeviceName  = id.DeviceName;
                    ThisDeviceRole  = DeviceRoleClassifier.RoleLabel(id.InferredRole);
                    ThisDeviceGlyph = DeviceRoleClassifier.RoleGlyph(id.InferredRole);
                    ThisDeviceColor = DeviceRoleClassifier.RoleColor(id.InferredRole);
                    ThisDeviceId    = id.DeviceId;
                    ThisDeviceOs    = id.OsVersion;
                });
            }).ConfigureAwait(true);

            RefreshFromRegistry();
            RefreshEcosystemData();
        }
        catch (Exception ex)
        {
            // WMI probe or registry read failed. Leave placeholder text in place
            // and let the page render with whatever data was already populated.
            System.Diagnostics.Debug.WriteLine($"[DeviceIntelVM] InitializeAsync failed: {ex.Message}");
        }
        finally
        {
            // Always clear the spinner — a stuck IsLoading = true permanently
            // hides the page content with no way to recover.
            IsLoading = false;
        }
    }

    // ── Live snapshot handler ─────────────────────────────────────────────────

    private void OnSnapshotReceived(object? _, SharedOperationalSnapshot snap)
    {
        _ui.TryEnqueue(() =>
        {
            RefreshFromRegistry();
            RefreshEcosystemData();
        });
    }

    // ── Data refresh ──────────────────────────────────────────────────────────

    private void RefreshFromRegistry()
    {
        var pairings  = _registry.GetAll();
        var snapshots = _sync.GetRemoteSnapshots();

        TrustedDevices.Clear();
        foreach (var p in pairings)
        {
            snapshots.TryGetValue(p.DeviceId, out var snap);
            TrustedDevices.Add(BuildDeviceCardVM(p, snap));
        }

        IsNoDevicesLinked = TrustedDevices.Count == 0;
        LinkedDeviceCount = TrustedDevices.Count switch
        {
            0 => "No linked devices",
            1 => "1 linked device",
            _ => $"{TrustedDevices.Count} linked devices",
        };

        OnPropertyChanged(nameof(DeviceListVisibility));
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    private void RefreshEcosystemData()
    {
        // Cross-machine insights
        var insights = _analytics.AnalyzeCurrentState();
        CrossInsights.Clear();
        foreach (var i in insights)
            CrossInsights.Add(BuildInsightVM(i));
        HasCrossInsights = CrossInsights.Count > 0;

        // Distributed timeline
        var events = _timelineAgg.BuildMergedTimeline();
        DistributedEvents.Clear();
        foreach (var ev in events.Take(50))
            DistributedEvents.Add(BuildTimelineEventVM(ev));
        HasDistributedEvents = DistributedEvents.Count > 0;

        // Ecosystem narrative
        EcosystemNarrative = _analytics.GenerateEcosystemNarrative();

        // Sync status
        var onlineCount = _sync.GetRemoteSnapshots().Count;
        SyncStatusText = onlineCount switch
        {
            0 => "No devices online",
            1 => "1 device synchronized",
            _ => $"{onlineCount} devices synchronized",
        };
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void StartPairing()
    {
        var code = _sync.BeginPairing();
        PairingCode6         = code[..3] + " " + code[3..];
        PairingInstructions  = $"On the other device, open Lucid → Device Intel → " +
                               $"Link a Device, then enter code: {PairingCode6}. " +
                               "Code expires in 5 minutes.";
        IsPairingMode = true;
    }

    [RelayCommand]
    private void CancelPairing()
    {
        _sync.CancelPairing();
        IsPairingMode = false;
        PairingCode6  = "——————";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            await Task.Delay(300).ConfigureAwait(true); // brief visual feedback
            RefreshFromRegistry();
            RefreshEcosystemData();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DeviceIntelVM] RefreshAsync failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void RemoveDevice(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return;
        _registry.Remove(deviceId);
        RefreshFromRegistry();
        RefreshEcosystemData();
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Cleanup()
    {
        _sync.SnapshotReceived -= OnSnapshotReceived;
    }

    // ── ViewModel builders ────────────────────────────────────────────────────

    private static TrustedDeviceCardViewModel BuildDeviceCardVM(
        StoredDevicePairing pairing,
        SharedOperationalSnapshot? snap)
    {
        Enum.TryParse<DeviceRole>(pairing.InferredRole, out var role);

        var isOnline   = snap is not null;
        var syncAge    = isOnline ? DateTimeOffset.UtcNow - pairing.LastSync : TimeSpan.Zero;
        var syncLabel  = isOnline ? "Online"
                       : pairing.LastSync == DateTimeOffset.MinValue ? "Never synced"
                       : $"Last seen {FormatAge(DateTimeOffset.UtcNow - pairing.LastSync)} ago";
        var syncColor  = isOnline ? "#22C55E" : "#6B7280";

        return new TrustedDeviceCardViewModel
        {
            DeviceId        = pairing.DeviceId,
            DeviceName      = pairing.DeviceName,
            RoleLabel       = DeviceRoleClassifier.RoleLabel(role),
            RoleGlyph       = DeviceRoleClassifier.RoleGlyph(role),
            RoleColor       = DeviceRoleClassifier.RoleColor(role),
            SyncStatusLabel = syncLabel,
            SyncStatusColor = syncColor,
            LastSyncText    = pairing.LastSync == DateTimeOffset.MinValue
                                  ? "—"
                                  : FormatAge(DateTimeOffset.UtcNow - pairing.LastSync) + " ago",
            WorkloadLabel   = snap is not null ? CrossMachineAnalyticsEngine.WorkloadLabel(snap.CurrentWorkload) : "—",
            WorkloadColor   = snap is not null ? WorkloadColor(snap.CurrentWorkload) : "#6B7280",
            UptimeText      = snap is not null ? FormatUptime(snap.SystemUptime) : "—",
            CpuText         = snap is not null ? $"{snap.CpuPercent:F0}%" : "—",
            RamText         = snap is not null ? $"{snap.RamPercent:F0}%" : "—",
            IsOnline        = isOnline,
        };
    }

    private static CrossInsightViewModel BuildInsightVM(CrossMachineInsight insight)
    {
        var (typeLabel, typeColor, typeGlyph) = insight.Type switch
        {
            CrossInsightType.SimultaneousLoad      => ("Simultaneous Load", "#EF4444", ""),
            CrossInsightType.ThermalDivergence     => ("Thermal Divergence", "#F59E0B", ""),
            CrossInsightType.CommonWorkload        => ("Common Workload",    "#6366F1", ""),
            CrossInsightType.DeviceSpecificAnomaly => ("Isolated Anomaly",   "#F97316", ""),
            CrossInsightType.SharedDegradation     => ("Shared Degradation", "#EF4444", ""),
            CrossInsightType.UpdateRegression      => ("Update Regression",  "#A855F7", ""),
            CrossInsightType.StorageGrowthPattern  => ("Storage Pattern",    "#10B981", ""),
            _                                      => ("Pattern",            "#6B7280", ""),
        };

        var (confLabel, confColor) = insight.Confidence switch
        {
            Services.Behavior.BehaviorConfidence.High   => ("High confidence",   "#22C55E"),
            Services.Behavior.BehaviorConfidence.Medium => ("Medium confidence", "#F59E0B"),
            _                                           => ("Low confidence",    "#6B7280"),
        };

        return new CrossInsightViewModel
        {
            InsightId       = insight.InsightId,
            Title           = insight.Title,
            Explanation     = insight.Explanation,
            TypeLabel       = typeLabel,
            TypeColor       = typeColor,
            TypeGlyph       = typeGlyph,
            ConfidenceLabel = confLabel,
            ConfidenceColor = confColor,
            AffectedDevices = $"{insight.AffectedDeviceIds.Count} device{(insight.AffectedDeviceIds.Count == 1 ? "" : "s")} affected",
            TimeText        = FormatAge(DateTimeOffset.UtcNow - insight.DetectedAt) + " ago",
        };
    }

    private static DistributedTimelineEventViewModel BuildTimelineEventVM(DistributedTimelineEvent ev)
    {
        return new DistributedTimelineEventViewModel
        {
            EventId          = ev.EventId,
            SourceDeviceName = ev.SourceDeviceName,
            Title            = ev.Title,
            Description      = ev.Description ?? string.Empty,
            Glyph            = ev.Glyph,
            AccentColor      = ev.AccentColor,
            TimeText         = FormatAge(DateTimeOffset.UtcNow - ev.OccurredAt) + " ago",
            IsLocalDevice    = ev.IsLocalDevice,
            IsCorrelated     = ev.IsCorrelated,
            DeviceBadge      = ev.IsLocalDevice ? "This device" : ev.SourceDeviceName,
            DeviceBadgeColor = ev.IsLocalDevice ? "#3B82F6" : "#6366F1",
        };
    }

    // ── Format helpers ────────────────────────────────────────────────────────

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 60) return "less than a minute";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m";
        if (age.TotalHours   < 24) return $"{(int)age.TotalHours}h";
        return $"{(int)age.TotalDays}d";
    }

    private static string FormatUptime(TimeSpan ts)
    {
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m";
        if (ts.TotalHours   < 24) return $"{ts.Hours}h {ts.Minutes}m";
        return $"{(int)ts.TotalDays}d";
    }

    private static string WorkloadColor(Services.Behavior.WorkloadType wl) => wl switch
    {
        Services.Behavior.WorkloadType.Gaming          => "#A855F7",
        Services.Behavior.WorkloadType.Development     => "#3B82F6",
        Services.Behavior.WorkloadType.MediaEditing    => "#F59E0B",
        Services.Behavior.WorkloadType.Rendering       => "#EF4444",
        Services.Behavior.WorkloadType.Streaming       => "#10B981",
        Services.Behavior.WorkloadType.BrowserResearch => "#6366F1",
        Services.Behavior.WorkloadType.Productivity    => "#22C55E",
        _                                              => "#6B7280",
    };
}
