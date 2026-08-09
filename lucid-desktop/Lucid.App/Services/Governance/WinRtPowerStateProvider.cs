using Windows.System.Power;

namespace Lucid.Services.Governance;

/// <summary>
/// WinRT-backed implementation of <see cref="IPowerStateProvider"/>.
///
/// Lives in Lucid.App because <c>Windows.System.Power.PowerManager</c> is a
/// WinRT type and Lucid.Core is platform-abstraction-free. The read is a cheap
/// cached property access and is safe to call off the UI thread.
///
/// Per the interface contract this never throws — PowerManager is unavailable
/// in some sandboxed contexts, and treating that as "plugged in" keeps the
/// governor running rather than failing a telemetry tick over a power query.
/// </summary>
internal sealed class WinRtPowerStateProvider : IPowerStateProvider
{
    public bool IsOnBattery
    {
        get
        {
            try
            {
                return PowerManager.BatteryStatus == BatteryStatus.Discharging;
            }
            catch
            {
                return false;
            }
        }
    }
}
