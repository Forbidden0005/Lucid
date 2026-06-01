//! lucid-scanner — native disk-scanner module for Lucid.
//!
//! Exposes a C-compatible API callable from C# via P/Invoke.
//!
//! Exported functions:
//!   lucid_scanner_version()         → null-terminated UTF-8 version string (static)
//!   lucid_scan_directory(...)       → recursive size + count scan
//!   lucid_scan_directory_top(...)   → top-N largest files in a tree
//!   lucid_free(ptr)                 → free heap strings returned by this library
//!
//! Safety contract for callers:
//!   • All `*const u8` path arguments must be valid null-terminated UTF-8.
//!   • Pointers written by this library (out-params, returned heap strings) must
//!     be freed with `lucid_free` — not with the C runtime free().
//!   • Functions are thread-safe: no shared mutable state.

mod scanner;

use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_int};
use scanner::{scan_directory, scan_top_files};

// ── Version ───────────────────────────────────────────────────────────────────

/// Returns a static null-terminated version string.
/// The returned pointer is valid for the lifetime of the process (static data).
#[no_mangle]
pub extern "C" fn lucid_scanner_version() -> *const c_char {
    b"lucid-scanner 0.1.0\0".as_ptr() as *const c_char
}

// ── Directory scan ────────────────────────────────────────────────────────────

/// Recursively scans `path` and writes total byte count and file count.
///
/// # Parameters
/// - `path`            Null-terminated UTF-8 path to scan.
/// - `out_total_bytes` Receives total bytes of all files found. Must not be null.
/// - `out_file_count`  Receives number of files found. Must not be null.
/// - `out_dir_count`   Receives number of directories found. Must not be null.
///
/// # Returns
/// - `0`  success
/// - `-1` path argument is null or not valid UTF-8
/// - `-2` I/O error (path does not exist or access denied)
#[no_mangle]
pub unsafe extern "C" fn lucid_scan_directory(
    path: *const c_char,
    out_total_bytes: *mut u64,
    out_file_count:  *mut u64,
    out_dir_count:   *mut u64,
) -> c_int {
    if path.is_null() || out_total_bytes.is_null() || out_file_count.is_null() || out_dir_count.is_null() {
        return -1;
    }

    let root = match unsafe { CStr::from_ptr(path) }.to_str() {
        Ok(s)  => s,
        Err(_) => return -1,
    };

    match scan_directory(root) {
        Ok(result) => {
            unsafe {
                *out_total_bytes = result.total_bytes;
                *out_file_count  = result.file_count;
                *out_dir_count   = result.dir_count;
            }
            0
        }
        Err(_) => -2,
    }
}

// ── Top-N largest files ───────────────────────────────────────────────────────

/// Scans `path` recursively and populates `out_entries` with the N largest files.
///
/// Each entry is a null-terminated UTF-8 string of the form:
///   `<absolute_path>\t<size_bytes>`
///
/// Memory for the strings is allocated on the Rust heap.
/// The caller must free each non-null pointer in `out_entries` with `lucid_free`,
/// then the array itself is caller-owned (stack or caller-allocated heap).
///
/// # Parameters
/// - `path`        Null-terminated UTF-8 root path.
/// - `n`           Maximum number of results (0 < n ≤ 1000).
/// - `out_entries` Caller-allocated array of `*mut c_char` with capacity ≥ n.
/// - `out_count`   Receives the actual number of entries written.
///
/// # Returns
/// - `0`  success
/// - `-1` bad arguments
/// - `-2` I/O error
#[no_mangle]
pub unsafe extern "C" fn lucid_scan_top_files(
    path: *const c_char,
    n: u32,
    out_entries: *mut *mut c_char,
    out_count: *mut u32,
) -> c_int {
    if path.is_null() || out_entries.is_null() || out_count.is_null() || n == 0 || n > 1000 {
        return -1;
    }

    let root = match unsafe { CStr::from_ptr(path) }.to_str() {
        Ok(s)  => s,
        Err(_) => return -1,
    };

    let top = match scan_top_files(root, n as usize) {
        Ok(v)  => v,
        Err(_) => return -2,
    };

    let written = top.len().min(n as usize);
    for (i, entry) in top.into_iter().take(written).enumerate() {
        let s = format!("{}\t{}", entry.path, entry.size_bytes);
        let cs = match CString::new(s) {
            Ok(c)  => c,
            Err(_) => continue,
        };
        unsafe { *out_entries.add(i) = cs.into_raw(); }
    }

    unsafe { *out_count = written as u32; }
    0
}

// ── Memory management ─────────────────────────────────────────────────────────

/// Frees a `*mut c_char` that was allocated by this library.
/// Passing a null pointer is a no-op.
/// Passing a pointer not originally from this library is undefined behaviour.
#[no_mangle]
pub unsafe extern "C" fn lucid_free(ptr: *mut c_char) {
    if !ptr.is_null() {
        unsafe { drop(CString::from_raw(ptr)); }
    }
}
