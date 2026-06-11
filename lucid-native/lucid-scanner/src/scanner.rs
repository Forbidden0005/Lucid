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

use std::cmp::Reverse;
use std::collections::BinaryHeap;
use std::io;
use windows_sys::Win32::Foundation::{ERROR_NO_MORE_FILES, HANDLE, INVALID_HANDLE_VALUE};
use windows_sys::Win32::Storage::FileSystem::{
    FindClose, FindExInfoBasic, FindExSearchNameMatch, FindFirstFileExW, FindNextFileW,
    FILE_ATTRIBUTE_DIRECTORY, FILE_ATTRIBUTE_REPARSE_POINT, FIND_FIRST_EX_LARGE_FETCH,
    WIN32_FIND_DATAW,
};

// ── Public types ──────────────────────────────────────────────────────────────

#[derive(Debug)]
pub struct ScanResult {
    pub total_bytes: u64,
    pub file_count: u64,
    pub dir_count: u64,
}

#[derive(Debug)]
pub struct TopFile {
    pub path: String,
    pub size_bytes: u64,
}

// ── scan_directory ────────────────────────────────────────────────────────────

/// Recursively totals bytes and counts files/dirs under `root`.
pub fn scan_directory(root: &str) -> io::Result<ScanResult> {
    validate_root(root)?;

    let mut result = ScanResult {
        total_bytes: 0,
        file_count: 0,
        dir_count: 0,
    };
    walk(root, &mut |_path, size| {
        if size == u64::MAX {
            result.dir_count += 1; // sentinel: directory
        } else {
            result.file_count += 1;
            result.total_bytes += size;
        }
    });
    Ok(result)
}

// ── scan_top_files ────────────────────────────────────────────────────────────

/// Returns the `n` largest files under `root`, sorted descending by size.
pub fn scan_top_files(root: &str, n: usize) -> io::Result<Vec<TopFile>> {
    validate_root(root)?;

    // Min-heap keyed by size so we can evict the smallest when the heap fills.
    let mut heap: BinaryHeap<Reverse<(u64, String)>> = BinaryHeap::new();

    walk(root, &mut |path, size| {
        if size == u64::MAX {
            return;
        } // skip directories
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
        .map(|Reverse((size, path))| TopFile {
            path,
            size_bytes: size,
        })
        .collect();

    results.sort_by_key(|file| Reverse(file.size_bytes));
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
                    let size =
                        (find_data.nFileSizeHigh as u64) << 32 | find_data.nFileSizeLow as u64;
                    cb(full, size);
                }
            }

            let ok = unsafe { FindNextFileW(handle, &mut find_data) };
            if ok == 0 {
                let err = unsafe { windows_sys::Win32::Foundation::GetLastError() };
                if err == ERROR_NO_MORE_FILES {
                    break;
                }
                break; // other error — stop iterating this dir
            }
        }

        unsafe {
            FindClose(handle);
        }
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

fn validate_root(root: &str) -> io::Result<()> {
    let metadata = std::fs::metadata(root)?;
    if !metadata.is_dir() {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "scan root must be a directory",
        ));
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{scan_directory, scan_top_files};
    use std::fs;
    use std::path::{Path, PathBuf};
    use std::time::{SystemTime, UNIX_EPOCH};

    struct TestDir {
        path: PathBuf,
    }

    impl TestDir {
        fn new() -> Self {
            let unique = SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .expect("system clock before unix epoch")
                .as_nanos();
            let path = std::env::temp_dir().join(format!("lucid-scanner-tests-{unique}"));
            fs::create_dir_all(&path).expect("create test root");
            Self { path }
        }

        fn path_str(&self) -> &str {
            self.path
                .to_str()
                .expect("test path should be valid unicode")
        }

        fn create_dir(&self, relative: &str) {
            fs::create_dir_all(self.path.join(relative)).expect("create subdirectory");
        }

        fn write_file(&self, relative: &str, bytes: &[u8]) {
            let full_path = self.path.join(relative);
            if let Some(parent) = full_path.parent() {
                fs::create_dir_all(parent).expect("create file parent");
            }

            fs::write(full_path, bytes).expect("write test file");
        }
    }

    impl Drop for TestDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.path);
        }
    }

    #[test]
    fn scan_directory_counts_files_directories_and_bytes() {
        let dir = TestDir::new();
        dir.create_dir("alpha");
        dir.create_dir("alpha\\nested");
        dir.write_file("root.txt", &[1, 2, 3]);
        dir.write_file("alpha\\nested\\child.bin", &[0; 7]);

        let result = scan_directory(dir.path_str()).expect("scan should succeed");

        assert_eq!(result.file_count, 2);
        assert_eq!(result.dir_count, 2);
        assert_eq!(result.total_bytes, 10);
    }

    #[test]
    fn scan_top_files_returns_descending_sizes_and_honors_limit() {
        let dir = TestDir::new();
        dir.write_file("small.txt", &[0; 3]);
        dir.write_file("medium.txt", &[0; 5]);
        dir.write_file("large.txt", &[0; 8]);

        let top = scan_top_files(dir.path_str(), 2).expect("scan should succeed");

        assert_eq!(top.len(), 2);
        assert_eq!(top[0].size_bytes, 8);
        assert_eq!(top[1].size_bytes, 5);
        assert!(Path::new(&top[0].path).ends_with("large.txt"));
        assert!(Path::new(&top[1].path).ends_with("medium.txt"));
    }

    #[test]
    fn scan_directory_returns_not_found_for_missing_root() {
        let missing = std::env::temp_dir().join("lucid-scanner-missing-root");
        let error = scan_directory(missing.to_str().expect("temp path unicode"))
            .expect_err("missing root should fail");

        assert_eq!(error.kind(), std::io::ErrorKind::NotFound);
    }

    #[test]
    fn scan_directory_rejects_file_root() {
        let dir = TestDir::new();
        dir.write_file("root.txt", &[1, 2, 3]);

        let file_root = dir.path.join("root.txt");
        let error = scan_directory(file_root.to_str().expect("unicode path"))
            .expect_err("file roots should be rejected");

        assert_eq!(error.kind(), std::io::ErrorKind::InvalidInput);
    }

    #[test]
    fn scan_directory_accepts_verbatim_prefixed_root() {
        // C# callers may hand us \\?\-prefixed (extended-length) roots. The walker
        // builds child paths by string concatenation, so the prefix must propagate
        // through the whole traversal and produce identical results to a plain root.
        let dir = TestDir::new();
        dir.create_dir("alpha");
        dir.write_file("alpha\\child.bin", &[0; 6]);
        dir.write_file("root.txt", &[1, 2]);

        let plain = scan_directory(dir.path_str()).expect("plain root scan");
        let verbatim_root = format!("\\\\?\\{}", dir.path_str());
        let verbatim = scan_directory(&verbatim_root).expect("verbatim root scan");

        assert_eq!(verbatim.file_count, plain.file_count);
        assert_eq!(verbatim.dir_count, plain.dir_count);
        assert_eq!(verbatim.total_bytes, plain.total_bytes);
    }

    #[test]
    fn scan_directory_counts_files_beyond_legacy_max_path() {
        // Trees deeper than the 260-char legacy MAX_PATH exist in real user profiles
        // (node_modules, nested backups). std::fs creates them fine because Rust's
        // standard library converts long paths to extended-length form internally;
        // the Win32 walker only traverses them when given the \\?\ prefix explicitly.
        let dir = TestDir::new();

        let segment = "long-path-segment-x";
        let deep: Vec<&str> = std::iter::repeat(segment).take(16).collect();
        let deep = deep.join("\\");
        dir.write_file(&format!("{deep}\\leaf.bin"), &[0; 5]);

        let deepest = dir.path.join(&deep);
        assert!(
            deepest.to_str().expect("unicode path").len() > 260,
            "test tree must exceed legacy MAX_PATH for this test to be meaningful"
        );

        let verbatim_root = format!("\\\\?\\{}", dir.path_str());
        let result = scan_directory(&verbatim_root).expect("verbatim long-path scan");

        assert_eq!(result.file_count, 1);
        assert_eq!(result.dir_count, 16);
        assert_eq!(result.total_bytes, 5);
    }

    #[test]
    fn scan_directory_skips_junction_cycles_and_terminates() {
        // A junction inside the tree pointing back at the root is the classic
        // infinite-recursion trap. The walker must skip reparse points entirely:
        // the scan terminates, and the junction is neither counted nor traversed.
        let dir = TestDir::new();
        dir.create_dir("real");
        dir.write_file("real\\file.txt", &[0; 4]);

        let link = dir.path.join("real").join("cycle");
        let status = std::process::Command::new("cmd")
            .arg("/C")
            .arg("mklink")
            .arg("/J")
            .arg(&link)
            .arg(&dir.path)
            .status()
            .expect("spawn cmd for mklink");
        assert!(
            status.success(),
            "mklink /J should succeed without elevation"
        );

        let result = scan_directory(dir.path_str()).expect("scan with junction cycle");

        assert_eq!(result.file_count, 1, "only the real file is counted");
        assert_eq!(
            result.dir_count, 1,
            "junction is skipped, not counted as a directory"
        );
        assert_eq!(result.total_bytes, 4);
    }

    #[test]
    fn scan_directory_handles_trailing_separator_root() {
        // Roots arriving from UI/file pickers often carry a trailing separator.
        // The walker special-cases this when building the find pattern.
        let dir = TestDir::new();
        dir.write_file("root.txt", &[1, 2, 3]);

        let with_sep = format!("{}\\", dir.path_str());
        let result = scan_directory(&with_sep).expect("trailing-separator scan");

        assert_eq!(result.file_count, 1);
        assert_eq!(result.total_bytes, 3);
    }
}
