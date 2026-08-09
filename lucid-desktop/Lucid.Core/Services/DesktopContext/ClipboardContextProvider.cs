using System.Runtime.InteropServices;
using System.Text;

namespace Lucid.Services.DesktopContext;

/// <summary>
/// Reads basic clipboard metadata via Win32 APIs.
/// Observation-only. Never persists clipboard content.
/// Text preview is truncated to 80 characters.
/// </summary>
internal static class ClipboardContextProvider
{
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_HDROP       = 15;
    private const uint UINT_MAX       = 0xFFFFFFFF;

    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? sb, uint cch);

    public static ClipboardContext GetCurrent()
    {
        bool hasText  = IsClipboardFormatAvailable(CF_UNICODETEXT);
        bool hasFiles = IsClipboardFormatAvailable(CF_HDROP);

        string? preview   = null;
        int     fileCount = 0;

        if (!OpenClipboard(IntPtr.Zero))
            return new ClipboardContext { HasText = hasText, HasFiles = hasFiles, UpdatedAt = DateTimeOffset.Now };

        try
        {
            if (hasText)
            {
                try
                {
                    var hData = GetClipboardData(CF_UNICODETEXT);
                    if (hData != IntPtr.Zero)
                    {
                        var ptr = GlobalLock(hData);
                        if (ptr != IntPtr.Zero)
                        {
                            try
                            {
                                var raw = Marshal.PtrToStringUni(ptr) ?? string.Empty;
                                // Truncate and sanitize — never store the full text
                                raw     = raw.Replace('\n', ' ').Replace('\r', ' ').Trim();
                                preview = raw.Length > 80 ? raw[..80] + "…" : raw;
                            }
                            finally { GlobalUnlock(hData); }
                        }
                    }
                }
                catch { /* read is best-effort */ }
            }

            if (hasFiles)
            {
                try
                {
                    var hDrop = GetClipboardData(CF_HDROP);
                    if (hDrop != IntPtr.Zero)
                        fileCount = (int)DragQueryFile(hDrop, UINT_MAX, null, 0);
                }
                catch { /* read is best-effort */ }
            }
        }
        finally { CloseClipboard(); }

        return new ClipboardContext
        {
            HasText     = hasText,
            HasFiles    = hasFiles,
            TextPreview = preview,
            FileCount   = fileCount,
            UpdatedAt   = DateTimeOffset.Now,
        };
    }
}
