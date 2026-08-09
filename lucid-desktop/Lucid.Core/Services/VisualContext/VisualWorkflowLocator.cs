using System.Runtime.InteropServices;
using System.Text;

namespace Lucid.Services.VisualContext;

/// <summary>
/// Locates operational UI elements within a window using child-window enumeration.
///
/// This powers the "show me where to click" and "highlight the next step" use-cases
/// by finding controls whose title text matches the query, then returning their
/// screen coordinates for the GuidedInteractionOverlay to use.
///
/// Implementation:
///   • EnumChildWindows walks all child HWNDs.
///   • GetWindowText + GetClassName identify each control.
///   • GetWindowRect provides screen-coordinate bounding rects.
///   • Fuzzy title matching (contains/starts-with) is used — no regex, no ML.
///
/// Safety rules:
///   • Never auto-clicks. Caller drives interaction.
///   • Returns coordinates only — the overlay uses them to guide, not act.
///   • Bounded enumeration (≤ 200 children total).
/// </summary>
public sealed class VisualWorkflowLocator
{
    // ── Win32 P/Invokes ───────────────────────────────────────────────────────

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Win32Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect { public int Left, Top, Right, Bottom; }

    // ── Predefined location queries ───────────────────────────────────────────

    // Maps a natural-language query keyword to the control text patterns to search for.
    private static readonly Dictionary<string, string[]> QueryMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["startup toggle"]        = ["On", "Off", "Enabled", "Disabled"],
            ["cleanup button"]        = ["Clean", "Cleanup", "Clear", "Delete", "Remove"],
            ["startup app"]           = ["Startup", "Autostart"],
            ["storage sense"]         = ["Storage Sense", "Run Storage Sense"],
            ["check for updates"]     = ["Check for updates", "Check now"],
            ["device manager"]        = ["Device Manager"],
            ["network adapter"]       = ["Ethernet", "Wi-Fi", "Network Adapter", "Wireless"],
            ["event viewer"]          = ["Event Viewer", "Windows Logs", "Application", "System"],
            ["ok button"]             = ["OK", "Confirm"],
            ["apply button"]          = ["Apply"],
            ["cancel button"]         = ["Cancel"],
            ["enable"]                = ["Enable", "Turn on", "On"],
            ["disable"]               = ["Disable", "Turn off", "Off"],
            ["save"]                  = ["Save", "Save changes"],
            ["close"]                 = ["Close", "X"],
        };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Locates UI elements in the given HWND matching the natural-language query.
    /// Returns up to 5 matches sorted by confidence (best match first).
    /// Never throws.
    /// </summary>
    public IReadOnlyList<VisualWorkflowTarget> LocateElements(IntPtr hwnd, string query)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            var patterns = ResolvePatterns(query);
            var candidates = new List<(double confidence, VisualWorkflowTarget target)>();

            EnumChildWindows(hwnd, (child, _) =>
            {
                if (candidates.Count >= 200) return false;
                if (!IsWindowVisible(child)) return true;

                var text = GetText(child);
                if (string.IsNullOrWhiteSpace(text)) return true;

                GetWindowRect(child, out Win32Rect r);
                var rect = new ScreenRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                if (rect.IsEmpty || rect.Width < 8) return true;

                var confidence = ScoreMatch(text, patterns, query);
                if (confidence > 0.3)
                {
                    candidates.Add((confidence, new VisualWorkflowTarget
                    {
                        Label       = text,
                        Description = BuildTargetDescription(text, GetClass(child)),
                        ScreenRect  = rect,
                        ActionHint  = BuildActionHint(text, query),
                        Confidence  = confidence,
                        WindowTitle = GetText(hwnd),
                    }));
                }

                return true;
            }, IntPtr.Zero);

            return candidates
                .OrderByDescending(c => c.confidence)
                .Take(5)
                .Select(c => c.target)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Returns the first matching element, or null if nothing found.
    /// Convenience wrapper over <see cref="LocateElements"/>.
    /// </summary>
    public VisualWorkflowTarget? LocatePrimary(IntPtr hwnd, string query)
    {
        var results = LocateElements(hwnd, query);
        return results.Count > 0 ? results[0] : null;
    }

    // ── Matching logic ────────────────────────────────────────────────────────

    private static string[] ResolvePatterns(string query)
    {
        // Check predefined map first
        foreach (var (key, patterns) in QueryMap)
        {
            if (query.Contains(key, StringComparison.OrdinalIgnoreCase))
                return patterns;
        }

        // Fall back to using words from the query as patterns
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static double ScoreMatch(string controlText, string[] patterns, string rawQuery)
    {
        var text = controlText.ToLowerInvariant();

        // Exact match
        if (text.Equals(rawQuery, StringComparison.OrdinalIgnoreCase)) return 1.0;

        double best = 0;
        foreach (var p in patterns)
        {
            var pattern = p.ToLowerInvariant();
            if (text == pattern)                          best = Math.Max(best, 1.0);
            else if (text.StartsWith(pattern))            best = Math.Max(best, 0.85);
            else if (text.Contains(pattern))              best = Math.Max(best, 0.65);
            else if (pattern.Contains(text) && text.Length > 2) best = Math.Max(best, 0.55);
        }
        return best;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildTargetDescription(string controlText, string className)
    {
        var ctrlType = className.ToLowerInvariant() switch
        {
            "button"  => "button",
            "edit"    => "text field",
            "static"  => "label",
            "listbox" => "list",
            "combobox" => "dropdown",
            _         => "control",
        };
        return $"'{controlText}' ({ctrlType})";
    }

    private static string BuildActionHint(string controlText, string query)
    {
        var t = controlText.ToLowerInvariant();
        if (t is "ok" or "apply" or "save")  return "Click to confirm";
        if (t is "cancel" or "close")        return "Click to cancel/close";
        if (t is "on" or "enable")           return "Toggle on";
        if (t is "off" or "disable")         return "Toggle off";
        return $"Click to interact with '{controlText}'";
    }

    private static string GetText(IntPtr hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(len + 2);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(128);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
