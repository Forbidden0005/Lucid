using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Lucid.Services.VisualContext;

/// <summary>
/// Classifies the active foreground window into a <see cref="SemanticWindowContext"/>
/// using Win32 P/Invoke only (no COM, no packages).
///
/// Analysis pipeline:
///   1. GetForegroundWindow()       → HWND
///   2. GetWindowText()             → title string
///   3. GetClassName()              → window class name
///   4. GetWindowThreadProcessId()  → PID → Process.GetProcessById() → process name
///   5. GetWindowRect()             → screen bounds
///   6. EnumChildWindows()          → detected controls (first 50, capped)
///   7. Classify()                  → SemanticWindowType + confidence + hints
///
/// Privacy rules:
///   • Reads only structural/metadata attributes (title, class, process name, rect).
///   • Does NOT read text content of arbitrary controls.
///   • Does NOT inspect browser navigation bars or document content.
///   • Control enumeration is bounded (≤ 50 direct children).
/// </summary>
public sealed class WindowSemanticAnalyzer
{
    // ── Win32 P/Invokes ───────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Win32Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    // EnumChildWindows — used to enumerate direct children for control discovery
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect { public int Left, Top, Right, Bottom; }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyses the current foreground window synchronously.
    /// Safe to call from any thread; all P/Invokes are thread-safe.
    /// </summary>
    public SemanticWindowContext AnalyzeActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        return hwnd == IntPtr.Zero ? UnknownContext(hwnd) : AnalyzeWindow(hwnd);
    }

    /// <summary>
    /// Analyses a specific HWND.
    /// </summary>
    public SemanticWindowContext AnalyzeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return UnknownContext(hwnd);

        var title     = GetTitle(hwnd);
        var className = GetClass(hwnd);
        var process   = GetProcessName(hwnd);
        var rect      = GetRect(hwnd);
        var controls  = EnumerateChildren(hwnd);

        var (type, confidence) = Classify(title, className, process);
        var (hints, actions)   = BuildHints(type, title, className, process);

        return new SemanticWindowContext
        {
            WindowTitle        = title,
            ProcessName        = process,
            ClassName          = className,
            WindowType         = type,
            Confidence         = confidence,
            WindowRect         = rect,
            Hwnd               = hwnd,
            ContextDescription = BuildDescription(type, title, process),
            WorkflowHints      = hints,
            SuggestedActions   = actions,
            DetectedControls   = controls,
        };
    }

    // ── Classification ────────────────────────────────────────────────────────

    private static (SemanticWindowType type, double confidence)
        Classify(string title, string className, string process)
    {
        var titleL   = title.ToLowerInvariant();
        var processL = process.ToLowerInvariant();
        var classL   = className.ToLowerInvariant();

        // ── File Explorer ─────────────────────────────────────────────────────
        if (classL == "cabinetclass" || classL == "explorerwindowclass")
            return (SemanticWindowType.FileExplorer, 0.97);

        // ── Windows Settings (UWP, shown as ApplicationFrameWindow) ──────────
        if (processL is "systemsettings" or "systemsettings.exe" ||
            (classL == "applicationframewindow" && ContainsAny(titleL, "settings")))
        {
            var page = SettingsPageInterpreter.ClassifyPageTitle(title);
            return page switch
            {
                SettingsPageType.StartupApps   => (SemanticWindowType.StartupSettings,   0.95),
                SettingsPageType.StorageSense  => (SemanticWindowType.StorageSettings,   0.95),
                SettingsPageType.PrivacySecurity => (SemanticWindowType.PrivacySettings, 0.95),
                SettingsPageType.Network       => (SemanticWindowType.NetworkSettings,   0.95),
                SettingsPageType.Display       => (SemanticWindowType.DisplaySettings,   0.95),
                SettingsPageType.WindowsUpdate => (SemanticWindowType.WindowsUpdate,     0.95),
                SettingsPageType.Bluetooth     => (SemanticWindowType.BluetoothSettings, 0.95),
                SettingsPageType.AppsFeatures  => (SemanticWindowType.AppsSettings,      0.95),
                SettingsPageType.WindowsSecurity=>(SemanticWindowType.SecuritySettings,  0.95),
                _                              => (SemanticWindowType.WindowsSettings,   0.90),
            };
        }

        // ── Device Manager ────────────────────────────────────────────────────
        if (ContainsAny(titleL, "device manager") || processL == "mmc" &&
            ContainsAny(titleL, "device"))
            return (SemanticWindowType.DeviceManager, 0.92);

        // ── Event Viewer ──────────────────────────────────────────────────────
        if (ContainsAny(titleL, "event viewer") ||
            processL == "mmc" && ContainsAny(titleL, "event"))
            return (SemanticWindowType.EventViewer, 0.92);

        // ── Task Manager ──────────────────────────────────────────────────────
        if (processL is "taskmgr" or "taskmgr.exe" || ContainsAny(titleL, "task manager"))
            return (SemanticWindowType.TaskManager, 0.95);

        // ── Services ─────────────────────────────────────────────────────────
        if (processL == "mmc" && ContainsAny(titleL, "services"))
            return (SemanticWindowType.ServicesManager, 0.90);

        // ── Registry Editor ───────────────────────────────────────────────────
        if (processL is "regedit" or "regedit.exe" || ContainsAny(titleL, "registry editor"))
            return (SemanticWindowType.RegistryEditor, 0.95);

        // ── Disk Management ───────────────────────────────────────────────────
        if (processL == "mmc" && ContainsAny(titleL, "disk management"))
            return (SemanticWindowType.DiskManagement, 0.90);

        // ── Control Panel ─────────────────────────────────────────────────────
        if (processL is "control" or "control.exe" || ContainsAny(titleL, "control panel"))
            return (SemanticWindowType.ControlPanel, 0.90);

        // ── Resource / Performance Monitor ────────────────────────────────────
        if (processL is "perfmon" or "perfmon.exe" || ContainsAny(titleL, "performance monitor"))
            return (SemanticWindowType.PerformanceMonitor, 0.90);

        if (processL is "resmon" or "resmon.exe" || ContainsAny(titleL, "resource monitor"))
            return (SemanticWindowType.ResourceMonitor, 0.90);

        // ── System Information ────────────────────────────────────────────────
        if (processL is "msinfo32" or "msinfo32.exe" || ContainsAny(titleL, "system information"))
            return (SemanticWindowType.SystemInformation, 0.90);

        // ── Windows Store ─────────────────────────────────────────────────────
        if (processL is "winstore.app" or "windowsstore" ||
            ContainsAny(titleL, "microsoft store"))
            return (SemanticWindowType.MicrosoftStore, 0.90);

        // ── Browsers ─────────────────────────────────────────────────────────
        if (processL is "chrome" or "firefox" or "msedge" or "iexplore" or
                        "opera" or "brave" or "vivaldi")
            return (SemanticWindowType.Browser, 0.95);

        // ── Terminals ─────────────────────────────────────────────────────────
        if (processL is "cmd" or "powershell" or "pwsh" or "windowsterminal" or
                        "wt" or "bash" or "git-bash")
            return (SemanticWindowType.Terminal, 0.95);

        // ── Lucid itself ──────────────────────────────────────────────────────
        if (processL is "lucid.app" or "lucid" || ContainsAny(titleL, "lucid"))
            return (SemanticWindowType.LucidApp, 0.95);

        // ── Open/Save dialogs ─────────────────────────────────────────────────
        if (classL is "#32770" or "bosa_sdm_msword" && ContainsAny(titleL, "open", "save"))
        {
            return ContainsAny(titleL, "save")
                ? (SemanticWindowType.SaveFileDialog, 0.85)
                : (SemanticWindowType.OpenFileDialog, 0.85);
        }

        // ── Generic system dialog ─────────────────────────────────────────────
        if (classL == "#32770")
            return (SemanticWindowType.SystemDialog, 0.70);

        return (SemanticWindowType.Unknown, 0.30);
    }

    // ── Hints & actions ───────────────────────────────────────────────────────

    private static (IReadOnlyList<string> hints, IReadOnlyList<string> actions)
        BuildHints(SemanticWindowType type, string title, string className, string process)
    {
        var hints   = new List<string>();
        var actions = new List<string>();

        switch (type)
        {
            case SemanticWindowType.FileExplorer:
                hints.Add("File Explorer is open — I can help you find and organise files.");
                actions.Add("Scan for large files in this folder");
                actions.Add("Identify duplicate files");
                actions.Add("Open Downloads folder");
                break;

            case SemanticWindowType.WindowsSettings:
                hints.Add("Windows Settings is open — I can guide you through configuration steps.");
                actions.Add("Show me the Startup Apps toggle");
                actions.Add("Guide me to Storage settings");
                break;

            case SemanticWindowType.StartupSettings:
                hints.Add("You are on the Startup Apps page — manage which apps launch at boot.");
                actions.Add("Disable a startup app");
                actions.Add("Identify high-impact startup apps");
                break;

            case SemanticWindowType.StorageSettings:
                hints.Add("You are on the Storage settings page.");
                actions.Add("Open Storage Sense");
                actions.Add("Check storage recommendations");
                break;

            case SemanticWindowType.DeviceManager:
                hints.Add("Device Manager is open — I can help identify driver issues.");
                actions.Add("Highlight network adapters");
                actions.Add("Show devices with warnings");
                break;

            case SemanticWindowType.TaskManager:
                hints.Add("Task Manager is open — I can correlate process activity with insights.");
                actions.Add("Highlight high-CPU processes");
                break;

            case SemanticWindowType.EventViewer:
                hints.Add("Event Viewer is open — I can help filter for operational errors.");
                actions.Add("Find recent error events");
                break;

            case SemanticWindowType.Terminal:
                hints.Add("A terminal is open.");
                break;

            case SemanticWindowType.Browser:
                hints.Add("A browser is in focus.");
                break;
        }

        return (hints, actions);
    }

    private static string BuildDescription(SemanticWindowType type, string title, string process) =>
        type switch
        {
            SemanticWindowType.FileExplorer        => $"File Explorer · {title}",
            SemanticWindowType.WindowsSettings     => $"Windows Settings · {title}",
            SemanticWindowType.StartupSettings     => "Windows Settings · Startup Apps",
            SemanticWindowType.StorageSettings     => "Windows Settings · Storage",
            SemanticWindowType.PrivacySettings     => "Windows Settings · Privacy & Security",
            SemanticWindowType.NetworkSettings     => "Windows Settings · Network",
            SemanticWindowType.WindowsUpdate       => "Windows Settings · Windows Update",
            SemanticWindowType.DeviceManager       => "Device Manager",
            SemanticWindowType.EventViewer         => "Event Viewer",
            SemanticWindowType.TaskManager         => "Task Manager",
            SemanticWindowType.Terminal            => $"Terminal · {process}",
            SemanticWindowType.Browser             => $"Browser · {process}",
            SemanticWindowType.RegistryEditor      => "Registry Editor",
            SemanticWindowType.LucidApp            => "Lucid",
            _                                      => string.IsNullOrEmpty(title) ? process : title,
        };

    // ── Child window enumeration ──────────────────────────────────────────────

    private static IReadOnlyList<DetectedControl> EnumerateChildren(IntPtr parent)
    {
        var list  = new List<DetectedControl>();
        const int maxControls = 50;

        EnumChildWindows(parent, (hwnd, _) =>
        {
            if (list.Count >= maxControls) return false;

            try
            {
                var cls  = GetClass(hwnd);
                var text = GetTitle(hwnd);
                GetWindowRect(hwnd, out Win32Rect r);

                // Skip controls with no meaningful text and tiny rects
                if (string.IsNullOrWhiteSpace(text) && r.Right - r.Left < 8)
                    return true;

                list.Add(new DetectedControl
                {
                    Name         = text,
                    ControlType  = MapClassToControlType(cls),
                    Text         = text,
                    IsEnabled    = true,
                    BoundingRect = new ScreenRect(r.Left, r.Top,
                                                  r.Right - r.Left, r.Bottom - r.Top),
                    ClassName    = cls,
                });
            }
            catch { /* skip this control */ }

            return true;
        }, IntPtr.Zero);

        return list;
    }

    private static string MapClassToControlType(string cls) => cls.ToLowerInvariant() switch
    {
        "button"        => "Button",
        "edit"          => "TextBox",
        "static"        => "Label",
        "combobox"      => "ComboBox",
        "listbox"       => "ListBox",
        "listview"      => "ListView",
        "treeview"      => "TreeView",
        "scrollbar"     => "ScrollBar",
        "msctls_progress32" => "ProgressBar",
        "tooltips_class32"  => "Tooltip",
        _               => cls,
    };

    // ── Win32 helpers ─────────────────────────────────────────────────────────

    private static string GetTitle(IntPtr hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(len + 2);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return string.Empty;
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return string.Empty; }
    }

    private static ScreenRect GetRect(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out Win32Rect r)) return ScreenRect.Empty;
        return new ScreenRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static SemanticWindowContext UnknownContext(IntPtr hwnd)
        => new()
        {
            WindowTitle        = string.Empty,
            ProcessName        = string.Empty,
            ClassName          = string.Empty,
            WindowType         = SemanticWindowType.Unknown,
            Confidence         = 0,
            WindowRect         = ScreenRect.Empty,
            Hwnd               = hwnd,
            ContextDescription = "No active window detected.",
        };

    private static bool ContainsAny(string source, params string[] terms)
        => terms.Any(t => source.Contains(t, StringComparison.OrdinalIgnoreCase));
}
