using System.Runtime.InteropServices;

namespace Lucid.Services.Telemetry.Samplers;

public interface IRamSampler
{
    RamSample Read();
}

public sealed record RamSample(double UsagePercent, long UsedMb, long TotalMb);

/// <summary>
/// Reads physical memory statistics via the kernel32 GlobalMemoryStatusEx API.
///
/// This is the lowest-overhead path to RAM data on Windows — a single syscall
/// with no counter warm-up, no WMI, and no polling delay. The result is always
/// current to the millisecond the call is made, making it suitable for 1-second
/// telemetry intervals without any accuracy loss.
///
/// dwMemoryLoad (0–100) from the OS is used for the usage percentage rather than
/// computing it from raw bytes, because the kernel accounts for memory compression
/// and standby-list reclamation in ways that a naive (used/total) calculation does
/// not reflect.
/// </summary>
public sealed class RamSampler : IRamSampler
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        internal uint  dwLength;
        internal uint  dwMemoryLoad;          // 0–100: current usage %
        internal ulong ullTotalPhys;          // total physical RAM, bytes
        internal ulong ullAvailPhys;          // available physical RAM, bytes
        internal ulong ullTotalPageFile;
        internal ulong ullAvailPageFile;
        internal ulong ullTotalVirtual;
        internal ulong ullAvailVirtual;
        internal ulong ullAvailExtendedVirtual;

        internal static MEMORYSTATUSEX Create() =>
            new() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public RamSample Read()
    {
        var status = MEMORYSTATUSEX.Create();
        if (!GlobalMemoryStatusEx(ref status))
            return new RamSample(0, 0, 0);

        long totalMb = (long)(status.ullTotalPhys >> 20);   // bytes → MiB
        long availMb = (long)(status.ullAvailPhys >> 20);
        long usedMb  = Math.Max(totalMb - availMb, 0);

        return new RamSample(
            UsagePercent: Math.Clamp(status.dwMemoryLoad, 0u, 100u),
            UsedMb:       usedMb,
            TotalMb:      totalMb);
    }
}
