namespace Lucid.Services.Infrastructure.OperationalState;

/// <summary>
/// Flags describing active runtime conditions.
/// Multiple conditions can be active simultaneously.
///
/// Conditions are reported by individual subsystems and aggregated by
/// <see cref="IOperationalStateCoordinator"/> into the platform snapshot.
/// </summary>
[Flags]
public enum RuntimeCondition
{
    /// <summary>No unusual conditions — system is operating normally.</summary>
    None = 0,

    /// <summary>CPU utilization is sustained above the high-pressure threshold.</summary>
    HighCpuPressure = 1 << 0,

    /// <summary>RAM utilization is sustained above the high-pressure threshold.</summary>
    HighMemoryPressure = 1 << 1,

    /// <summary>Available disk space on the system drive is critically low.</summary>
    LowDiskSpace = 1 << 2,

    /// <summary>Thermal readings indicate the CPU or GPU is running hot.</summary>
    ThermalStress = 1 << 3,

    /// <summary>One or more workflows are actively executing.</summary>
    WorkflowActive = 1 << 4,

    /// <summary>A consent request is pending user response.</summary>
    ConsentPending = 1 << 5,

    /// <summary>One or more services are in degraded or failed state.</summary>
    ServiceDegradation = 1 << 6,

    /// <summary>The operational simulation engine is running a scenario.</summary>
    SimulationRunning = 1 << 7,

    /// <summary>The companion conversation session is actively processing a turn.</summary>
    ConversationActive = 1 << 8,

    /// <summary>The platform is running on battery and battery-aware mode is active.</summary>
    BatteryAwareMode = 1 << 9,

    /// <summary>A background automation sequence is executing.</summary>
    AutomationActive = 1 << 10,
}
