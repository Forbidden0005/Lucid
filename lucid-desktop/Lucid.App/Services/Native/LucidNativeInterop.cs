using System.Runtime.InteropServices;

namespace Lucid.Services.Native;

/// <summary>
/// Raw P/Invoke declarations for lucid_scanner.dll.
///
/// These are the unsafe bindings — call through <see cref="NativeScannerService"/>
/// instead of using these directly.
///
/// DLL search order: the DLL must be in the app's output directory or on PATH.
/// The build pipeline copies lucid_scanner.dll from lucid-native/target/release/
/// into the app output directory.
/// </summary>
internal static partial class LucidNativeInterop
{
    private const string DllName = "lucid_scanner";

    // ── Version ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a pointer to a static null-terminated version string.
    /// The pointer is valid for the lifetime of the process — do not free it.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "lucid_scanner_version")]
    [return: MarshalAs(UnmanagedType.LPStr)]
    internal static partial string lucid_scanner_version();

    // ── Directory scan ────────────────────────────────────────────────────────

    /// <summary>
    /// Recursively scans <paramref name="path"/> and writes totals to out-params.
    /// </summary>
    /// <returns>0 on success, -1 bad args, -2 I/O error.</returns>
    [LibraryImport(DllName, EntryPoint = "lucid_scan_directory",
                   StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lucid_scan_directory(
        string    path,
        out ulong outTotalBytes,
        out ulong outFileCount,
        out ulong outDirCount);

    // ── Top-N largest files ───────────────────────────────────────────────────

    /// <summary>
    /// Fills <paramref name="outEntries"/> with pointers to heap strings of the form
    /// <c>absolute_path\tsize_bytes</c>. Each non-null pointer must be freed with
    /// <see cref="lucid_free"/>.
    /// </summary>
    /// <returns>0 on success, -1 bad args, -2 I/O error.</returns>
    [LibraryImport(DllName, EntryPoint = "lucid_scan_top_files",
                   StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int lucid_scan_top_files(
        string path,
        uint   n,
        IntPtr* outEntries,
        out uint outCount);

    // ── Memory management ─────────────────────────────────────────────────────

    /// <summary>
    /// Frees a string pointer returned by this library.
    /// Passing null is safe.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "lucid_free")]
    internal static partial void lucid_free(IntPtr ptr);
}
