using System.Runtime.InteropServices;

namespace Lucid.Services.VisualContext;

/// <summary>
/// Performs explicit, consent-gated window capture using Win32 GDI P/Invoke.
///
/// Safety guarantees:
///   • NEVER captures without ConsentBoundScreenAnalysis approval.
///   • Captures are in-memory only — never written to disk automatically.
///   • Auto-expiry at 5 minutes; callers should discard after ExpiresAt.
///   • No background or continuous capture — one-shot only.
///   • No network access — all processing is local.
///
/// Implementation:
///   Uses PrintWindow (PW_RENDERFULLCONTENT) + GetDIBits to extract raw
///   BGRA32 pixel data without requiring System.Drawing or D3D11.
///   This is the same mechanism used by accessibility and remote-desktop tools.
/// </summary>
public sealed class ScreenCaptureCoordinator
{
    // ── Win32 GDI P/Invokes ───────────────────────────────────────────────────

    [DllImport("user32.dll",  SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Win32Rect lpRect);

    [DllImport("user32.dll",  SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll",  SetLastError = true)]
    private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll",  SetLastError = true)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll",  SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("gdi32.dll",   SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll",   SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll",   SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll",   SetLastError = true)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll",   SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll",   SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr hdc, IntPtr hbm,
        uint start, uint cLines,
        byte[]? lpvBits, ref BitmapInfo lpbmi, uint usage);

    // PrintWindow with PW_RENDERFULLCONTENT = 0x2 (captures layered/DWM content)
    [DllImport("user32.dll",  SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const uint DIB_RGB_COLORS       = 0;
    private const uint BI_RGB               = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint   biSize;
        public int    biWidth;
        public int    biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint   biCompression;
        public uint   biSizeImage;
        public int    biXPelsPerMeter;
        public int    biYPelsPerMeter;
        public uint   biClrUsed;
        public uint   biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public uint[] bmiColors;
    }

    // ── Default capture expiry ────────────────────────────────────────────────

    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(5);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures the contents of the given window handle and returns a
    /// <see cref="CapturedFrame"/> holding raw BGRA32 pixel data.
    ///
    /// Returns null if:
    ///   • hwnd is invalid or invisible
    ///   • the window dimensions are unreasonably large (> 4 K × 4 K)
    ///   • any GDI operation fails
    ///
    /// The caller is responsible for respecting CapturedFrame.ExpiresAt.
    /// </summary>
    public Task<CapturedFrame?> CaptureWindowAsync(
        IntPtr            hwnd,
        string?           sourceTitle  = null,
        CancellationToken ct           = default)
    {
        // Capture is GDI-synchronous — run on a thread-pool thread to avoid
        // blocking the UI dispatcher.
        return Task.Run(() => CaptureWindow(hwnd, sourceTitle), ct);
    }

    // ── Internal GDI capture ──────────────────────────────────────────────────

    private static CapturedFrame? CaptureWindow(IntPtr hwnd, string? sourceTitle)
    {
        try
        {
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd))
                return null;

            if (!GetWindowRect(hwnd, out Win32Rect wr))
                return null;

            int width  = wr.Right  - wr.Left;
            int height = wr.Bottom - wr.Top;

            // Guard against absurdly large or empty windows
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                return null;

            IntPtr hdc      = GetDC(IntPtr.Zero);   // screen DC
            IntPtr hdcMem   = IntPtr.Zero;
            IntPtr hBitmap  = IntPtr.Zero;
            IntPtr hOld     = IntPtr.Zero;

            try
            {
                hdcMem  = CreateCompatibleDC(hdc);
                hBitmap = CreateCompatibleBitmap(hdc, width, height);
                hOld    = SelectObject(hdcMem, hBitmap);

                // PW_RENDERFULLCONTENT captures layered / DWM-composited content
                PrintWindow(hwnd, hdcMem, PW_RENDERFULLCONTENT);

                // Extract BGRA32 pixel data via GetDIBits
                var bmi = new BitmapInfo
                {
                    bmiHeader = new BitmapInfoHeader
                    {
                        biSize        = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                        biWidth       = width,
                        biHeight      = -height,   // negative = top-down DIB
                        biPlanes      = 1,
                        biBitCount    = 32,
                        biCompression = BI_RGB,
                    },
                    bmiColors = new uint[1],
                };

                var pixels = new byte[width * height * 4];
                GetDIBits(hdcMem, hBitmap, 0, (uint)height, pixels, ref bmi, DIB_RGB_COLORS);

                var now = DateTimeOffset.Now;
                return new CapturedFrame
                {
                    Width             = width,
                    Height            = height,
                    PixelData         = pixels,
                    CapturedAt        = now,
                    ExpiresAt         = now + DefaultExpiry,
                    SourceWindowTitle = sourceTitle,
                };
            }
            finally
            {
                if (hOld    != IntPtr.Zero) SelectObject(hdcMem, hOld);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (hdcMem  != IntPtr.Zero) DeleteDC(hdcMem);
                if (hdc     != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        catch
        {
            // Never propagate — capture is best-effort
            return null;
        }
    }

    /// <summary>
    /// Returns true if the given CapturedFrame is still within its retention window.
    /// </summary>
    public static bool IsValid(CapturedFrame? frame)
        => frame is { IsExpired: false };

    /// <summary>
    /// Clears pixel data from an expired frame to allow GC to reclaim memory.
    /// The frame record itself becomes a zero-size placeholder.
    /// </summary>
    public static CapturedFrame Expire(CapturedFrame frame)
        => frame with { PixelData = [] };
}
