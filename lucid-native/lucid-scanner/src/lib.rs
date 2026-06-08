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

#[cfg(test)]
mod tests {
    use super::{lucid_free, lucid_scan_directory, lucid_scan_top_files, lucid_scanner_version};
    use std::ffi::{CStr, CString};
    use std::os::raw::c_char;
    use std::ptr;
    use std::time::{SystemTime, UNIX_EPOCH};

    fn create_test_directory() -> std::path::PathBuf {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system clock before unix epoch")
            .as_nanos();
        let root = std::env::temp_dir().join(format!("lucid-ffi-tests-{unique}"));
        std::fs::create_dir_all(&root).expect("create ffi test root");
        std::fs::write(root.join("one.txt"), [1_u8, 2, 3, 4]).expect("write one.txt");
        std::fs::create_dir_all(root.join("nested")).expect("create nested directory");
        std::fs::write(root.join("nested").join("two.bin"), [0_u8; 9]).expect("write two.bin");
        root
    }

    #[test]
    fn version_string_is_stable_utf8() {
        let version = unsafe { CStr::from_ptr(lucid_scanner_version()) }
            .to_str()
            .expect("version should be valid utf8");

        assert_eq!(version, "lucid-scanner 0.1.0");
    }

    #[test]
    fn directory_scan_rejects_null_arguments() {
        let mut total_bytes = 0_u64;
        let mut file_count = 0_u64;
        let mut dir_count = 0_u64;

        let rc = unsafe {
            lucid_scan_directory(
                ptr::null(),
                &mut total_bytes,
                &mut file_count,
                &mut dir_count,
            )
        };

        assert_eq!(rc, -1);
    }

    #[test]
    fn directory_scan_reports_missing_root_as_io_error() {
        let path = CString::new("C:\\definitely-missing-lucid-scanner-root").expect("cstring");
        let mut total_bytes = 123_u64;
        let mut file_count = 456_u64;
        let mut dir_count = 789_u64;

        let rc = unsafe {
            lucid_scan_directory(
                path.as_ptr(),
                &mut total_bytes,
                &mut file_count,
                &mut dir_count,
            )
        };

        assert_eq!(rc, -2);
        assert_eq!(total_bytes, 123);
        assert_eq!(file_count, 456);
        assert_eq!(dir_count, 789);
    }

    #[test]
    fn top_files_scan_rejects_invalid_n() {
        let path = CString::new("C:\\").expect("cstring");
        let mut count = 0_u32;
        let mut entries: [*mut c_char; 1] = [ptr::null_mut(); 1];

        let rc = unsafe {
            lucid_scan_top_files(path.as_ptr(), 0, entries.as_mut_ptr(), &mut count)
        };

        assert_eq!(rc, -1);
        assert_eq!(count, 0);
    }

    #[test]
    fn top_files_scan_returns_owned_strings_and_count() {
        let root = create_test_directory();
        let root_cstr = CString::new(root.to_str().expect("unicode path")).expect("cstring");
        let mut entries: [*mut c_char; 4] = [ptr::null_mut(); 4];
        let mut count = 0_u32;

        let rc = unsafe {
            lucid_scan_top_files(root_cstr.as_ptr(), 4, entries.as_mut_ptr(), &mut count)
        };

        assert_eq!(rc, 0);
        assert_eq!(count, 2);

        let mut values = Vec::new();
        for entry in entries.iter().take(count as usize) {
            assert!(!entry.is_null());

            let value = unsafe { CStr::from_ptr(*entry) }
                .to_str()
                .expect("entry should be utf8")
                .to_owned();
            values.push(value);

            unsafe { lucid_free(*entry); }
        }

        assert!(values[0].contains("\t9"));
        assert!(values[1].contains("\t4"));
        assert!(values[0].contains("two.bin"));
        assert!(values[1].contains("one.txt"));

        std::fs::remove_dir_all(root).expect("cleanup ffi test root");
    }
}
