using System.Runtime.InteropServices;

namespace Lucid.Services.DesktopContext;

/// <summary>
/// Reads the active File Explorer window's current path and selected items
/// via Shell.Application COM dispatch.
///
/// COM requires STA. This provider owns a dedicated STA background thread.
/// Results are cached in a volatile field; the timer thread reads the cache.
///
/// Observation-only. Does NOT scan drives or index files.
/// </summary>
internal sealed class ExplorerContextProvider : IDisposable
{
    private volatile ExplorerContext? _lastResult;
    private readonly Thread           _staThread;
    private volatile bool             _stop;
    private readonly ManualResetEventSlim _wake = new(false);

    public ExplorerContextProvider()
    {
        _staThread = new Thread(StaLoop)
        {
            IsBackground = true,
            Name         = "DesktopCtx-STA",
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
    }

    public ExplorerContext? GetCurrent() => _lastResult;

    public void Wake() => _wake.Set();

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
    }

    // ── STA thread loop ────────────────────────────────────────────────────────

    private void StaLoop()
    {
        while (!_stop)
        {
            _wake.Wait(TimeSpan.FromSeconds(2));
            _wake.Reset();
            if (_stop) break;

            try { _lastResult = QueryExplorer(); }
            catch { /* COM can throw; retain last good result */ }
        }
    }

    private static ExplorerContext? QueryExplorer()
    {
        // Find the foreground window first so we can match the active explorer
        var foregroundHwnd = GetForegroundWindow();

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return null;

        dynamic shell   = Activator.CreateInstance(shellType)!;
        dynamic windows = shell.Windows();
        int     count   = (int)windows.Count;

        for (int i = 0; i < count; i++)
        {
            dynamic? window = null;
            try { window = windows.Item(i); } catch { continue; }
            if (window is null) continue;

            // Match the foreground window by HWND
            try
            {
                var hwnd = (IntPtr)(long)window.HWND;
                if (hwnd != foregroundHwnd) continue;
            }
            catch { continue; }

            // LocationURL is "file:///C:/Users/..."
            string? locationUrl = null;
            try { locationUrl = (string?)window.LocationURL; } catch { /* best-effort: Explorer window/COM object may close mid-read — context omitted */ }
            if (string.IsNullOrEmpty(locationUrl)) continue;

            string? path = null;
            try { path = new Uri(locationUrl).LocalPath; } catch { /* best-effort: malformed location URL — path context omitted */ }
            if (string.IsNullOrEmpty(path)) continue;

            // Selected items
            var selectedNames = new List<string>();
            try
            {
                dynamic selectedItems = window.Document.SelectedItems();
                int selCount = (int)selectedItems.Count;
                for (int j = 0; j < Math.Min(selCount, 20); j++)
                {
                    dynamic item = selectedItems.Item(j);
                    string  name = (string)item.Name;
                    if (!string.IsNullOrEmpty(name)) selectedNames.Add(name);
                }
            }
            catch { /* selection read is best-effort */ }

            return BuildContext(path, selectedNames);
        }

        return null;
    }

    private static ExplorerContext BuildContext(string path, List<string> selected)
    {
        var lower = path.ToLowerInvariant();

        bool isDownloads = lower.Contains(@"\downloads");
        bool isDocs      = lower.Contains(@"\documents") || lower.Contains(@"\onedrive");
        bool isMedia     = lower.Contains(@"\pictures") || lower.Contains(@"\videos")
                        || lower.Contains(@"\music")    || lower.Contains(@"\photos");
        bool isProject   = lower.Contains(@"\source")   || lower.Contains(@"\repos")
                        || lower.Contains(@"\projects")  || lower.Contains(@"\dev")
                        || lower.Contains(@"\code")      || lower.Contains(@"\git");

        return new ExplorerContext
        {
            CurrentPath       = path,
            SelectedItems     = selected,
            IsDownloadsFolder = isDownloads,
            IsDocumentsFolder = isDocs,
            IsMediaFolder     = isMedia,
            IsProjectFolder   = isProject,
        };
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
