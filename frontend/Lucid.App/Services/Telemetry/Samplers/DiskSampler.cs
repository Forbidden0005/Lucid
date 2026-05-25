using System.Diagnostics;

namespace Lucid.Services.Telemetry.Samplers;

public interface IDiskSampler : IDisposable
{
    /// <summary>Create counters and issue warm-up reads. Call once before Read().</summary>
    void Initialize();
    DiskSample Read();
}

public sealed record DiskSample(double ReadMbps, double WriteMbps, long UsedGb, long TotalGb);

/// <summary>
/// Reads disk I/O throughput and storage capacity.
///
/// Throughput (read/write MB/s):
///   "PhysicalDisk \ Disk Read Bytes/sec" and "Disk Write Bytes/sec" with the
///   "_Total" instance aggregate all physical disks. Values are returned in
///   bytes/sec by the counter; this sampler converts to MB/s.
///
/// Storage capacity (used / total GB):
///   Enumerated once via DriveInfo across all fixed local drives and cached.
///   Re-read every CapacityRefreshInterval ticks to catch external drives being
///   connected without hammering the VFS on every 1-second poll.
///
/// First-read caveat:
///   Rate counters return 0 on the first NextValue(); the WindowsTelemetryService
///   warm-up call in Initialize() absorbs this so callers always receive real data.
/// </summary>
public sealed class DiskSampler : IDiskSampler
{
    private const string Category      = "PhysicalDisk";
    private const string TotalInstance = "_Total";

    // Refresh free space every 60 ticks (~60 seconds at 1 Hz) — it changes slowly.
    private const int CapacityRefreshInterval = 60;
    private int _capacityTickCounter;

    private PerformanceCounter? _readCounter;
    private PerformanceCounter? _writeCounter;

    private long _cachedUsedGb;
    private long _cachedTotalGb;

    public void Initialize()
    {
        try
        {
            _readCounter  = new PerformanceCounter(Category, "Disk Read Bytes/sec",  TotalInstance, readOnly: true);
            _writeCounter = new PerformanceCounter(Category, "Disk Write Bytes/sec", TotalInstance, readOnly: true);
        }
        catch
        {
            _readCounter?.Dispose();
            _readCounter  = null;
            _writeCounter = null;
        }

        RefreshCapacity();

        // Warm-up: discard the mandatory first 0.0 from rate counters.
        _ = _readCounter?.NextValue();
        _ = _writeCounter?.NextValue();
    }

    public DiskSample Read()
    {
        double readMbps  = 0;
        double writeMbps = 0;

        try
        {
            if (_readCounter is not null)
                readMbps = Math.Max(_readCounter.NextValue() / (1024.0 * 1024.0), 0);
        }
        catch
        {
            _readCounter?.Dispose();
            _readCounter = null;
        }

        try
        {
            if (_writeCounter is not null)
                writeMbps = Math.Max(_writeCounter.NextValue() / (1024.0 * 1024.0), 0);
        }
        catch
        {
            _writeCounter?.Dispose();
            _writeCounter = null;
        }

        if (++_capacityTickCounter >= CapacityRefreshInterval)
        {
            _capacityTickCounter = 0;
            RefreshCapacity();
        }

        return new DiskSample(readMbps, writeMbps, _cachedUsedGb, _cachedTotalGb);
    }

    private void RefreshCapacity()
    {
        long totalBytes = 0;
        long freeBytes  = 0;

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                if (!drive.IsReady) continue;

                totalBytes += drive.TotalSize;
                freeBytes  += drive.TotalFreeSpace;
            }
        }
        catch { }

        _cachedTotalGb = totalBytes >> 30;   // bytes → GiB
        _cachedUsedGb  = (totalBytes - freeBytes) >> 30;
    }

    public void Dispose()
    {
        _readCounter?.Dispose();
        _writeCounter?.Dispose();
    }
}
