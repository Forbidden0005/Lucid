//! Safe Rust scanning logic — no unsafe except the unavoidable Win32 boundary.
//!
//! Uses `FindFirstFileExW` / `FindNextFileW` directly rather than
//! `std::fs::read_dir` for two reasons:
//!   1. `FIND_FIRST_EX_LARGE_FETCH` batches directory reads and is ~3× faster
//!      on large trees than the per-entry fallback in std.
//!   2. We can skip reparse-point (junction/symlink) subtrees to avoid cycles
//!      without an extra `lstat` call per entry.
//!
//! The public API is entirely safe — callers in lib.rs handle the C boundary.

use std::collections::BinaryHeap;
use std::cmp::Reverse;
use windows_sys::Win32::Foundation::{
    ERROR_NO_MORE_FILES, HANDLE, INVALID_HANDLE_VALUE,
};
use windows_sys::Win32::Storage::FileSystem::{
    FindClose, FindFirstFileExW, FindNextFileW,
    WIN32_FIND_DATAW,
    FindExInfoBasic, FindExSearchNameMatch,
    FIND_FIRST_EX_LARGE_FETCH,
    FILE_ATTRIBUTE_DIRECTORY, FILE_ATTRIBUTE_REPARSE_POINT,
};

// ── Public types ──────────────────────────────────────────────────────────────

pub struct ScanResult {
    pub total_bytes: u64,
    pub file_count:  u64,
    pub dir_count:   u64,
}

pub struct TopFile {
    pub path:       String,
    pub size_bytes: u64,
}

// ── scan_directory ────────────────────────────────────────────────────────────

/// Recursively totals bytes and counts files/dirs under `root`.
pub fn scan_directory(root: &str) -> std::io::Result<ScanResult> {
    let mut result = ScanResult { total_bytes: 0, file_count: 0, dir_count: 0 };
    walk(root, &mut |_path, size| {
        if size == u64::MAX {
            result.dir_count += 1;   // sentinel: directory
        } else {
            result.file_count  += 1;
            result.total_bytes += size;
        }
    });
    Ok(result)
}

// ── scan_top_files ────────────────────────────────────────────────────────────

/// Returns the `n` largest files under `root`, sorted descending by size.
pub fn scan_top_files(root: &str, n: usize) -> std::io::Result<Vec<TopFile>> {
    // Min-heap keyed by size so we can evict the smallest when the heap fills.
    let mut heap: BinaryHeap<Reverse<(u64, String)>> = BinaryHeap::new();

    walk(root, &mut |path, size| {
        if size == u64::MAX { return; }    // skip directories
        if heap.len() < n {
            heap.push(Reverse((size, path)));
        } else if let Some(Reverse((min_size, _))) = heap.peek() {
            if size > *min_size {
                heap.pop();
                heap.push(Reverse((size, path)));
            }
        }
    });

    let mut results: Vec<TopFile> = heap
        .into_iter()
        .map(|Reverse((size, path))| TopFile { path, size_bytes: size })
        .collect();

    results.sort_by(|a, b| b.size_bytes.cmp(&a.size_bytes));
    Ok(results)
}

// ── Win32 walker ──────────────────────────────────────────────────────────────

/// Iterative directory walk using Win32 `FindFirstFileExW` / `FindNextFileW`.
///
/// Calls `cb(absolute_path, size_bytes)` for every file.
/// For directories, calls `cb(absolute_path, u64::MAX)` as a sentinel.
///
/// Skips reparse points (junctions / symlinks) to avoid cycles.
/// Skips `.` and `..` entries automatically.
fn walk(root: &str, cb: &mut impl FnMut(String, u64)) {
    // Iterative DFS — avoids stack overflow on very deep trees.
    let mut stack: Vec<String> = vec![root.to_owned()];

    while let Some(dir) = stack.pop() {
        let pattern = if dir.ends_with('\\') || dir.ends_with('/') {
            format!("{dir}*")
        } else {
            format!("{dir}\\*")
        };

        let pattern_w = to_wide_null(&pattern);
        let mut find_data: WIN32_FIND_DATAW = unsafe { std::mem::zeroed() };

        let handle: HANDLE = unsafe {
            FindFirstFileExW(
                pattern_w.as_ptr(),
                FindExInfoBasic,
                &mut find_data as *mut _ as *mut _,
                FindExSearchNameMatch,
                std::ptr::null(),
                FIND_FIRST_EX_LARGE_FETCH,
            )
        };

        if handle == INVALID_HANDLE_VALUE {
            continue; // access denied or does not exist — skip silently
        }

        loop {
            let name = wide_null_to_string(&find_data.cFileName);

            if name != "." && name != ".." {
                let full = format!("{dir}\\{name}");
                let attrs = find_data.dwFileAttributes;

                if attrs & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
                    // Skip symlinks / junctions to avoid cycles.
                } else if attrs & FILE_ATTRIBUTE_DIRECTORY != 0 {
                    cb(full.clone(), u64::MAX);
                    stack.push(full);
                } else {
                    let size = (find_data.nFileSizeHigh as u64) << 32
                        | find_data.nFileSizeLow as u64;
                    cb(full, size);
                }
            }

            let ok = unsafe { FindNextFileW(handle, &mut find_data) };
            if ok == 0 {
                let err = unsafe { windows_sys::Win32::Foundation::GetLastError() };
                if err == ERROR_NO_MORE_FILES { break; }
                break; // other error — stop iterating this dir
            }
        }

        unsafe { FindClose(handle); }
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

fn to_wide_null(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

fn wide_null_to_string(buf: &[u16]) -> String {
    let len = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
    String::from_utf16_lossy(&buf[..len])
}
