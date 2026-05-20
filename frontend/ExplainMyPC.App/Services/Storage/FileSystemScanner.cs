namespace ExplainMyPC.Services.Storage;

/// <summary>
/// Low-priority BFS file-system scanner.
///
/// Design:
///   • Thread.CurrentThread.Priority is set to BelowNormal for the duration
///     of the scan so it never competes with the UI thread.
///   • Yields FileInfo lazily — callers process each file immediately and
///     never hold the full enumeration in memory.
///   • Inaccessible directories and files are silently skipped.
///   • Cancellation is checked before each directory level.
///   • Configurable exclusion prefixes (case-insensitive) stop subtrees
///     like WinSxS and node_modules from being traversed.
///
/// Threading:
///   Always call from a Task.Run context — never on the UI thread.
/// </summary>
internal static class FileSystemScanner
{
    // Default directories to skip — huge, system-managed, not user data
    private static readonly IReadOnlyList<string> s_defaultExclusions =
    [
        @"\Windows\WinSxS",
        @"\Windows\System32",
        @"\Windows\SysWOW64",
        @"\Windows\Installer",
        @"\$Recycle.Bin",
        @"\$WINDOWS.~BT",
        @"\$WinREAgent",
        @"\System Volume Information",
        @"\ProgramData\Microsoft\Windows\WER",
        @"\node_modules",
        @"\.git\objects",
        @"\ExplainMyPC\Rollback",
    ];

    /// <summary>
    /// Enumerates all files under <paramref name="rootPath"/> breadth-first,
    /// skipping system subtrees and inaccessible paths.
    /// </summary>
    /// <param name="rootPath">Root to scan.</param>
    /// <param name="ct">Cancellation token — stops enumeration cleanly.</param>
    /// <param name="extraExclusions">
    /// Additional path substrings to exclude (case-insensitive),
    /// appended to the default exclusion list.
    /// </param>
    /// <param name="progressCallback">
    /// Optional callback invoked with (filesProcessed, bytesProcessed) every
    /// ~500 files to feed a progress UI without per-file callback overhead.
    /// </param>
    internal static IEnumerable<FileInfo> Enumerate(
        string            rootPath,
        CancellationToken ct,
        IEnumerable<string>? extraExclusions     = null,
        Action<int, long>?   progressCallback    = null)
    {
        if (!Directory.Exists(rootPath)) yield break;

        // Build exclusion set
        var exclusions = new List<string>(s_defaultExclusions);
        if (extraExclusions is not null)
            exclusions.AddRange(extraExclusions);

        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

        var queue = new Queue<string>();
        queue.Enqueue(rootPath);

        int  filesEmitted  = 0;
        long bytesEmitted  = 0;
        int  sinceCallback = 0;

        while (queue.Count > 0)
        {
            if (ct.IsCancellationRequested) yield break;

            var dir = queue.Dequeue();

            // Skip excluded subtrees
            if (IsExcluded(dir, exclusions)) continue;

            FileInfo[] files;
            try
            {
                files = new DirectoryInfo(dir).GetFiles();
            }
            catch { continue; }

            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) yield break;

                yield return file;

                filesEmitted++;
                sinceCallback++;

                try { bytesEmitted += file.Length; }
                catch { }

                if (sinceCallback >= 500 && progressCallback is not null)
                {
                    progressCallback(filesEmitted, bytesEmitted);
                    sinceCallback = 0;
                }
            }

            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (!IsExcluded(sub, exclusions))
                        queue.Enqueue(sub);
                }
            }
            catch { }
        }

        Thread.CurrentThread.Priority = ThreadPriority.Normal;
    }

    private static bool IsExcluded(string path, List<string> exclusions)
    {
        foreach (var ex in exclusions)
        {
            if (path.Contains(ex, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
