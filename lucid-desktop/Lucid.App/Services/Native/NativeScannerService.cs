using System.Runtime.InteropServices;

namespace Lucid.Services.Native;

/// <summary>
/// Results from a native directory scan.
/// </summary>
public sealed record DirectoryScanResult(
    string Root,
    ulong  TotalBytes,
    ulong  FileCount,
    ulong  DirCount)
{
    public double TotalGigabytes => TotalBytes / 1_073_741_824.0;
    public string TotalSizeLabel => TotalBytes switch
    {
        >= 1_073_741_824 => $"{TotalGigabytes:F2} GB",
        >= 1_048_576     => $"{TotalBytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{TotalBytes / 1_024.0:F0} KB",
        _                => $"{TotalBytes} B",
    };
}

/// <summary>
/// A single large-file entry returned by <see cref="NativeScannerService.GetTopFilesAsync"/>.
/// </summary>
public sealed record LargeFileEntry(string Path, ulong SizeBytes)
{
    public string SizeLabel => SizeBytes switch
    {
        >= 1_073_741_824 => $"{SizeBytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576     => $"{SizeBytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{SizeBytes / 1_024.0:F0} KB",
        _                => $"{SizeBytes} B",
    };
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}

/// <summary>
/// Safe managed wrapper around the native lucid_scanner.dll.
///
/// Uses <see cref="LucidNativeInterop"/> for raw P/Invoke calls. All
/// unsafe memory management is encapsulated here — callers see only
/// clean managed types.
///
/// Availability: The native DLL may not be present in all configurations.
/// Check <see cref="IsAvailable"/> before calling any scan method. When
/// unavailable, the C# <see cref="Storage.FileSystemScanner"/> is the fallback.
///
/// Thread safety: all public methods are safe to call from background threads.
/// </summary>
public sealed class NativeScannerService
{
    private static readonly Lazy<bool> s_available = new(ProbeAvailability);

    /// <summary>
    /// True if lucid_scanner.dll loaded successfully and the version call succeeded.
    /// Safe to check on any thread.
    /// </summary>
    public static bool IsAvailable => s_available.Value;

    /// <summary>
    /// Version string from the native library, or "unavailable" if not loaded.
    /// </summary>
    public static string NativeVersion { get; private set; } = "unavailable";

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Asynchronously scans <paramref name="rootPath"/> and returns totals.
    /// The scan runs on the thread pool so it never blocks the UI thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">If the native library is unavailable.</exception>
    /// <exception cref="IOException">If the native scanner returns an I/O error, or an
    /// internal error (a panic caught at the FFI boundary) — both signal the caller
    /// to fall back to the managed scanner.</exception>
    public Task<DirectoryScanResult> ScanDirectoryAsync(
        string rootPath, CancellationToken ct = default) =>
        Task.Run(() => ScanDirectoryCore(rootPath), ct);

    /// <summary>
    /// Asynchronously returns the <paramref name="topN"/> largest files under
    /// <paramref name="rootPath"/>, sorted descending by size.
    /// </summary>
    /// <exception cref="InvalidOperationException">If the native library is unavailable.</exception>
    public Task<IReadOnlyList<LargeFileEntry>> GetTopFilesAsync(
        string rootPath, int topN = 50, CancellationToken ct = default) =>
        Task.Run(() => GetTopFilesCore(rootPath, topN), ct);

    // ── Core (thread-pool) ─────────────────────────────────────────────────────

    private static DirectoryScanResult ScanDirectoryCore(string rootPath)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("lucid_scanner.dll is not available.");

        int rc = LucidNativeInterop.lucid_scan_directory(
            rootPath,
            out ulong totalBytes,
            out ulong fileCount,
            out ulong dirCount);

        return rc switch
        {
            0  => new DirectoryScanResult(rootPath, totalBytes, fileCount, dirCount),
            -2 => throw new IOException($"Native scanner: access denied or path not found: {rootPath}"),
            // -3 = a panic was caught at the FFI boundary. Per the native contract
            // it's an internal failure to be treated like I/O — so callers fall back
            // to the managed scanner rather than seeing a spurious "bad arguments".
            -3 => throw new IOException($"Native scanner: internal error while scanning: {rootPath}"),
            _  => throw new ArgumentException($"Native scanner: bad arguments for path: {rootPath}"),
        };
    }

    private static IReadOnlyList<LargeFileEntry> GetTopFilesCore(string rootPath, int topN)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("lucid_scanner.dll is not available.");

        if (topN <= 0 || topN > 1000)
            throw new ArgumentOutOfRangeException(nameof(topN), "Must be 1–1000.");

        uint n      = (uint)topN;
        uint count  = 0;
        var entries = new List<LargeFileEntry>();

        // Stack-allocate an array of IntPtr for the out-param entries.
        unsafe
        {
            var ptrs = stackalloc IntPtr[(int)n];
            int rc   = LucidNativeInterop.lucid_scan_top_files(
                rootPath, n, ptrs, out count);

            if (rc == -2) throw new IOException(
                $"Native scanner: access denied or path not found: {rootPath}");
            // -3 = panic caught at the FFI boundary — an internal failure treated
            // like I/O so callers fall back to the managed scanner (see the native
            // contract in lucid-scanner/src/lib.rs), not "bad arguments".
            if (rc == -3) throw new IOException(
                $"Native scanner: internal error while scanning: {rootPath}");
            if (rc != 0)  throw new ArgumentException(
                $"Native scanner: bad arguments for path: {rootPath}");

            for (int i = 0; i < (int)count; i++)
            {
                IntPtr ptr = ptrs[i];
                if (ptr == IntPtr.Zero) continue;

                try
                {
                    string? raw = Marshal.PtrToStringUTF8(ptr);
                    if (raw is not null)
                    {
                        int tab = raw.IndexOf('\t');
                        if (tab > 0 &&
                            ulong.TryParse(raw.AsSpan(tab + 1), out ulong size))
                        {
                            entries.Add(new LargeFileEntry(raw[..tab], size));
                        }
                    }
                }
                finally
                {
                    LucidNativeInterop.lucid_free(ptr);
                }
            }
        }

        return entries.AsReadOnly();
    }

    // ── Availability probe ─────────────────────────────────────────────────────

    private static bool ProbeAvailability()
    {
        try
        {
            NativeVersion = LucidNativeInterop.lucid_scanner_version();
            return !string.IsNullOrEmpty(NativeVersion);
        }
        catch (DllNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine(
                "[NativeScannerService] lucid_scanner.dll not found — falling back to managed scanner.");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[NativeScannerService] probe failed: {ex.Message}");
            return false;
        }
    }
}
