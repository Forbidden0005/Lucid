using System.Runtime.InteropServices;
using System.Text;

namespace Lucid.Services.DesktopContext;

/// <summary>
/// Reads the currently focused foreground window using Win32 APIs.
/// Observation-only. No hooks, no injection, no monitoring agents.
/// </summary>
internal static class ActiveWindowTracker
{
    // Win32 imports
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder name, ref int size);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static ActiveWindowContext? GetCurrent()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            var titleSb = new StringBuilder(256);
            GetWindowText(hwnd, titleSb, titleSb.Capacity);
            var title = titleSb.ToString();

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;

            var processName = GetProcessName(pid);

            return new ActiveWindowContext
            {
                ProcessName  = processName,
                WindowTitle  = title,
                AppCategory  = ClassifyApp(processName),
                DetectedAt   = DateTimeOffset.Now,
            };
        }
        catch { return null; }
    }

    private static string GetProcessName(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return string.Empty;
        try
        {
            var sb   = new StringBuilder(512);
            int size = sb.Capacity;
            if (QueryFullProcessImageName(handle, 0, sb, ref size) && size > 0)
                return Path.GetFileNameWithoutExtension(sb.ToString());
            return string.Empty;
        }
        finally { CloseHandle(handle); }
    }

    private static AppCategory ClassifyApp(string name) => name.ToLowerInvariant() switch
    {
        "explorer"                                           => AppCategory.Explorer,
        "chrome" or "msedge" or "firefox" or "brave"
            or "opera" or "vivaldi" or "iexplore"           => AppCategory.Browser,
        "devenv" or "code" or "notepad" or "notepad++"
            or "sublime_text" or "atom" or "rider"
            or "idea64" or "webstorm64" or "clion64"        => AppCategory.Editor,
        "cmd" or "powershell" or "pwsh" or "wt"
            or "windowsterminal" or "conhost"               => AppCategory.Terminal,
        "vlc" or "wmplayer" or "spotify" or "groove"
            or "musicapp" or "films & tv" or "mpv"          => AppCategory.Media,
        "systemsettings" or "mmc" or "control"
            or "taskschd" or "regedit"                      => AppCategory.Settings,
        _                                                    => AppCategory.Unknown,
    };
}
