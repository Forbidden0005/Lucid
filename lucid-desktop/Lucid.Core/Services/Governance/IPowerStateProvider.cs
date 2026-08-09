namespace Lucid.Services.Governance;

/// <summary>
/// Battery-state seam for the runtime governor.
///
/// <see cref="RuntimeGovernanceService"/> needs to know whether the machine is
/// running on battery so it can drop into LowPower mode and stop scheduling
/// background work. The only way to answer that on Windows is a WinRT call
/// (<c>Windows.System.Power.PowerManager</c>), which would pin the governor to
/// Lucid.App and put the mode-transition logic out of reach of tests.
///
/// Implementations:
///   • <c>Lucid.Services.Governance.WinRtPowerStateProvider</c> (Lucid.App)
///     wraps the real PowerManager.
///   • Tests supply a stub to drive battery transitions deterministically.
///
/// Implementations must not throw: WinRT power APIs are unavailable in some
/// sandboxed contexts, and the correct fallback is "assume plugged in" rather
/// than failing the governance tick.
/// </summary>
public interface IPowerStateProvider
{
    /// <summary>
    /// <see langword="true"/> when the machine is discharging (running on
    /// battery). Returns <see langword="false"/> when plugged in, when there is
    /// no battery, or when the platform cannot answer.
    /// </summary>
    bool IsOnBattery { get; }
}
