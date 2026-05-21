using ExplainMyPC.Services.Behavior;

namespace ExplainMyPC.Services.Distributed;

// ── Device role enumeration ───────────────────────────────────────────────────

/// <summary>Inferred operational role of a device on the local ecosystem.</summary>
public enum DeviceRole
{
    Unknown         = 0,
    GamingDesktop   = 1,
    Workstation     = 2,
    MediaServer     = 3,
    Laptop          = 4,
    SecondaryDevice = 5,
    AlwaysOnNode    = 6,
    HomeServer      = 7,
}

/// <summary>Current synchronization state with a remote device.</summary>
public enum DeviceSyncState { Offline, Discovering, Connecting, Synced, Stale }

/// <summary>Trust relationship state for a remote device pairing.</summary>
public enum DeviceTrustState { Untrusted, PairingPending, Trusted, Revoked }

/// <summary>Category of a cross-machine analytical insight.</summary>
public enum CrossInsightType
{
    SharedDegradation,
    UpdateRegression,
    CommonWorkload,
    EnvironmentalCorrelation,
    DeviceSpecificAnomaly,
    ThermalDivergence,
    SimultaneousLoad,
    StorageGrowthPattern,
}

// ── Identity records ──────────────────────────────────────────────────────────

/// <summary>
/// Stable identity for a device on the local operational ecosystem.
/// Generated once via <see cref="DeviceIdentityService"/> and persisted to
/// %LOCALAPPDATA%\ExplainMyPC\device_id.txt across sessions.
/// </summary>
public sealed record DeviceIdentity(
    string         DeviceId,
    string         DeviceName,
    string         OsVersion,
    string         MachineName,
    DeviceRole     InferredRole,
    int            CpuCoreCount,
    long           RamBytes,
    bool           HasBattery,
    bool           HasDedicatedGpu,
    DateTimeOffset ProfiledAt);

/// <summary>A trusted remote device with pairing and sync metadata.</summary>
public sealed record TrustedDevice(
    DeviceIdentity   Identity,
    DeviceTrustState TrustState,
    DeviceSyncState  SyncState,
    DateTimeOffset   PairedAt,
    DateTimeOffset   LastSync,
    string?          LastKnownAddress);

// ── Sync payload ──────────────────────────────────────────────────────────────

/// <summary>
/// Operational snapshot exchanged between paired devices over the local network.
/// Contains only aggregated operational signals — no raw file lists, no PII,
/// no process names, no user data.
/// Bounded to approximately 2 KB as JSON to keep sync traffic negligible.
/// </summary>
public sealed record SharedOperationalSnapshot(
    string                SourceDeviceId,
    string                DeviceName,
    DeviceRole            Role,
    DateTimeOffset        SnapshotAt,
    WorkloadType          CurrentWorkload,
    double                CpuPercent,
    double                RamPercent,
    double                CpuTemperatureCelsius,
    bool                  TemperatureAvailable,
    double                StorageUsedPercent,
    int                   ActiveInsightCount,
    TimeSpan              SystemUptime,
    string?               WorkloadRationale,
    IReadOnlyList<string> TopActiveInsightIds);

// ── Analytics records ─────────────────────────────────────────────────────────

/// <summary>
/// An analytical insight derived from comparing two or more linked devices.
/// All insights are deterministic and local-only — no ML, no cloud inference.
/// </summary>
public sealed record CrossMachineInsight(
    string                InsightId,
    string                Title,
    string                Explanation,
    IReadOnlyList<string> AffectedDeviceIds,
    CrossInsightType      Type,
    DateTimeOffset        DetectedAt,
    BehaviorConfidence    Confidence);

/// <summary>Privacy and trust summary for display in the Device Trust section.</summary>
public sealed record DeviceTrustProfile(
    string                DeviceId,
    string                DeviceName,
    DeviceTrustState      TrustState,
    bool                  IsEncryptionEnabled,
    IReadOnlyList<string> SharedDataCategories,
    DateTimeOffset        PairedAt,
    string                PrivacyGuarantee);

/// <summary>A timeline event attributed to a specific device in the distributed ecosystem.</summary>
public sealed record DistributedTimelineEvent(
    string         EventId,
    string         SourceDeviceId,
    string         SourceDeviceName,
    DateTimeOffset OccurredAt,
    string         Title,
    string?        Description,
    string         Glyph,
    string         AccentColor,
    bool           IsLocalDevice,
    bool           IsCorrelated,
    string?        CorrelationGroupId);

/// <summary>Side-by-side operational comparison between this device and a remote device.</summary>
public sealed record DeviceComparisonResult(
    string         DeviceIdA,
    string         DeviceNameA,
    string         DeviceIdB,
    string         DeviceNameB,
    double         AvgCpuA,
    double         AvgCpuB,
    double         AvgTemperatureA,
    double         AvgTemperatureB,
    double         StorageUsedPctA,
    double         StorageUsedPctB,
    WorkloadType   DominantWorkloadA,
    WorkloadType   DominantWorkloadB,
    DateTimeOffset ComputedAt);

// ── Persistence record ────────────────────────────────────────────────────────

/// <summary>Pairing record serialized to trusted_devices.json in %LOCALAPPDATA%\ExplainMyPC\.</summary>
public sealed record StoredDevicePairing(
    string         DeviceId,
    string         DeviceName,
    string         InferredRole,
    string         AesKeyBase64,
    string         LastKnownAddress,
    DateTimeOffset PairedAt,
    DateTimeOffset LastSync);
