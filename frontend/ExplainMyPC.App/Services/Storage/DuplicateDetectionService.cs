using System.Security.Cryptography;

namespace ExplainMyPC.Services.Storage;

/// <summary>
/// Two-phase duplicate detection:
///   Phase 1 — group files by exact size (free, O(n)).
///   Phase 2 — for each size group with 2+ files, compute MD5 hash and
///              group by hash. Only files that share a size ever get hashed.
///
/// Design:
///   • MD5 is used for speed — collision resistance is not required since we
///     always surface duplicates to the user before any deletion occurs.
///   • Hashing is streamed in 64 KB chunks to avoid loading large files into RAM.
///   • Cancellation is checked between files.
///   • Files that cannot be read (locked, access denied) are skipped silently.
///   • Minimum group waste is 1 MB to suppress noise from tiny duplicates
///     (e.g. dozens of identical zero-byte placeholder files).
/// </summary>
internal static class DuplicateDetectionService
{
    private const long MinGroupWasteBytes = 1 * 1_048_576; // 1 MB
    private const int  HashBufferSize     = 65_536;         // 64 KB

    /// <summary>
    /// Finds duplicate groups among <paramref name="files"/>.
    /// </summary>
    /// <param name="files">
    /// Pre-collected file records. Should already be filtered to a reasonable
    /// minimum size (e.g. > 100 KB) to skip tiny files.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="progressCallback">
    /// Optional callback invoked with (filesHashed, totalToHash).
    /// </param>
    internal static IReadOnlyList<DuplicateFileGroup> Detect(
        IReadOnlyList<LargeFileRecord> files,
        CancellationToken              ct,
        Action<int, int>?              progressCallback = null)
    {
        // Phase 1: group by size
        var bySize = files
            .GroupBy(f => f.SizeBytes)
            .Where(g => g.Count() >= 2)
            .ToList();

        if (bySize.Count == 0) return [];

        var candidates = bySize.SelectMany(g => g).ToList();
        int totalToHash = candidates.Count;
        int hashed      = 0;

        // Phase 2: hash each candidate, group by hash
        var hashGroups = new Dictionary<string, List<LargeFileRecord>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var record in candidates)
        {
            if (ct.IsCancellationRequested) break;

            var hash = TryComputeHash(record.FullPath);
            hashed++;

            if (hash is not null)
            {
                if (!hashGroups.TryGetValue(hash, out var group))
                    hashGroups[hash] = group = new List<LargeFileRecord>();
                group.Add(record);
            }

            if (hashed % 20 == 0)
                progressCallback?.Invoke(hashed, totalToHash);
        }

        progressCallback?.Invoke(hashed, totalToHash);

        // Build result: only groups with 2+ files and meaningful waste
        return hashGroups
            .Where(kv => kv.Value.Count >= 2)
            .Select(kv =>
            {
                // Sort: keep the oldest-modified file as the "original"
                var sorted = kv.Value
                    .OrderBy(f => f.LastModifiedUtc)
                    .ToList();
                return new DuplicateFileGroup(kv.Key, sorted);
            })
            .Where(g => g.WasteSizeBytes >= MinGroupWasteBytes)
            .OrderByDescending(g => g.WasteSizeBytes)
            .ToList()
            .AsReadOnly();
    }

    private static string? TryComputeHash(string path)
    {
        try
        {
            using var md5    = MD5.Create();
            using var stream = File.Open(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var buffer = new byte[HashBufferSize];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                md5.TransformBlock(buffer, 0, read, null, 0);

            md5.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(md5.Hash!).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}
