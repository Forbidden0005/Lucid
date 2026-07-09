using System.Management;

namespace Lucid.Services.ProcessIntel;

/// <summary>Per-PID data only obtainable via a bulk WMI process query.</summary>
internal sealed class ProcessWmiInfo
{
    public int    ParentProcessId   { get; init; }
    public string ParentProcessName { get; init; } = string.Empty;
    public string CommandLine       { get; init; } = string.Empty;
}

/// <summary>
/// Maintains a PID → (parent PID, parent name, command line) map via a single
/// bulk <c>Win32_Process</c> WMI query, refreshed on a throttled interval.
///
/// Rationale: .NET's <see cref="System.Diagnostics.Process"/> exposes neither
/// parent PID nor command line for other processes — WMI is the only user-mode
/// source. A per-process WMI query per telemetry tick would be far too
/// expensive (COM marshalling per call), so this does one bulk query for all
/// processes and throttles it to <see cref="RefreshIntervalSeconds"/>, in line
/// with the project's resource-governance doctrine — this is a background,
/// not foreground, signal and must not compete with time-sensitive work.
///
/// Caveat (documented, not solved here): Win32_Process.ParentProcessId can be
/// stale/misleading if the parent PID has been reused after the parent exited.
/// Good enough for a heuristic signal; not treated as ground truth.
/// </summary>
internal sealed class ProcessParentMap
{
    private const int RefreshIntervalSeconds = 4;

    private readonly object _lock = new();
    private Dictionary<int, ProcessWmiInfo> _byPid = new();
    private DateTime _lastRefresh = DateTime.MinValue;

    /// <summary>Re-queries WMI if the throttle interval has elapsed. Safe to call every tick.</summary>
    internal void RefreshIfDue()
    {
        if ((DateTime.UtcNow - _lastRefresh).TotalSeconds < RefreshIntervalSeconds) return;
        _lastRefresh = DateTime.UtcNow;

        try { RefreshCore(); }
        catch { /* WMI can fail transiently (service restart, permissions) — keep stale cache */ }
    }

    internal ProcessWmiInfo? TryGet(int pid)
    {
        lock (_lock) return _byPid.TryGetValue(pid, out var info) ? info : null;
    }

    private void RefreshCore()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process");

        var names = new Dictionary<int, string>();
        var raw   = new List<(int Pid, int Ppid, string Cmd)>();

        foreach (ManagementObject obj in searcher.Get())
        {
            try
            {
                int pid  = (int)(uint)obj["ProcessId"];
                int ppid = (int)(uint)obj["ParentProcessId"];
                string name = obj["Name"] as string ?? string.Empty;
                string cmd  = obj["CommandLine"] as string ?? string.Empty;

                names[pid] = name;
                raw.Add((pid, ppid, cmd));
            }
            catch { /* a single row failing to parse shouldn't drop the whole snapshot */ }
            finally { obj.Dispose(); }
        }

        var next = new Dictionary<int, ProcessWmiInfo>(raw.Count);
        foreach (var (pid, ppid, cmd) in raw)
        {
            names.TryGetValue(ppid, out var parentName);
            next[pid] = new ProcessWmiInfo
            {
                ParentProcessId   = ppid,
                ParentProcessName = parentName ?? string.Empty,
                CommandLine       = cmd,
            };
        }

        lock (_lock) { _byPid = next; }
    }
}
