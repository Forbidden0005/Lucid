using System.Text.RegularExpressions;

namespace Lucid.Services.Storage;

/// <summary>
/// Near-duplicate detection — flags file pairs that are *likely* redundant even
/// though their bytes differ, using three strategies:
///
///   1. Copy patterns   — "report (2).docx", "photo_copy.jpg", "notes-v3.txt"
///                        sharing a base name in the same directory.
///   2. Format variants — same normalized name, different extension within a
///                        known content family (e.g. .png / .jpg, .wav / .mp3).
///   3. Name similarity — normalized names ≥ 80% similar in the same directory,
///                        with sizes within 2× of each other.
///
/// Unlike <see cref="DuplicateDetectionService"/> (exact hash groups), these are
/// heuristic matches. They are surfaced for manual review only — never wired to
/// a delete action — and every match carries a plain-English reason and a
/// confidence score so the user can judge each pair.
///
/// Performance guards: files under 10 KB are ignored, pairwise comparison is
/// bucketed by directory, and buckets are capped at the 500 largest files.
/// </summary>
internal static partial class NearDuplicateDetectionService
{
    private const double NameSimilarityThreshold = 0.80;
    private const double SizeProximityRatio      = 0.50;
    private const long   MinFileSizeBytes        = 10 * 1_024;
    private const int    MaxBucketSize           = 500;
    private const int    MaxResults              = 200;

    // Extensions that commonly hold the same content in different formats.
    private static readonly string[][] s_formatVariantGroups =
    [
        [".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif", ".webp", ".heic", ".heif"],
        [".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg"],
        [".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus"],
        [".doc", ".docx", ".odt", ".rtf", ".pdf"],
        [".xls", ".xlsx", ".ods", ".csv"],
        [".ppt", ".pptx", ".odp"],
        [".zip", ".tar", ".gz", ".bz2", ".7z", ".rar", ".xz"],
    ];

    [GeneratedRegex(@"[\s_-]*(copy|copia|kopia)[\s_-]*\d*", RegexOptions.IgnoreCase)]
    private static partial Regex CopyWordRegex();

    [GeneratedRegex(@"[\s_-]*\(\d+\)")]
    private static partial Regex NumberSuffixRegex();

    [GeneratedRegex(@"[\s_-]+v\d+", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffixRegex();

    [GeneratedRegex(@"[\s_-]+(final|draft|old|new|backup)", RegexOptions.IgnoreCase)]
    private static partial Regex StatusWordRegex();

    [GeneratedRegex(@"[\s_.-]+")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"^(.+?)[\s_-]*(copy|copia|kopia|\(\d+\)|v\d+|final|draft|old|new|backup)[\s_-]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex CopyPatternRegex();

    /// <summary>
    /// Finds likely near-duplicate pairs among <paramref name="files"/>,
    /// excluding pairs whose paths both appear in
    /// <paramref name="exactDuplicatePaths"/> (already reported as exact
    /// duplicates). Results are sorted by estimated redundant bytes, capped
    /// at <see cref="MaxResults"/>.
    /// </summary>
    internal static IReadOnlyList<NearDuplicateMatch> Detect(
        IReadOnlyList<LargeFileRecord> files,
        IReadOnlySet<string>           exactDuplicatePaths,
        CancellationToken              ct)
    {
        var eligible = files.Where(f => f.SizeBytes >= MinFileSizeBytes).ToList();

        var matches   = new List<NearDuplicateMatch>();
        var seenPairs = new HashSet<(string, string)>();

        foreach (var strategy in new Func<List<LargeFileRecord>, CancellationToken, IEnumerable<NearDuplicateMatch>>[]
                 { FindCopyPatterns, FindFormatVariants, FindNameSimilarityMatches })
        {
            if (ct.IsCancellationRequested) break;

            foreach (var match in strategy(eligible, ct))
            {
                var key = PairKey(match.FileA.FullPath, match.FileB.FullPath);
                if (seenPairs.Add(key) &&
                    !(exactDuplicatePaths.Contains(match.FileA.FullPath) &&
                      exactDuplicatePaths.Contains(match.FileB.FullPath)))
                {
                    matches.Add(match);
                }
            }
        }

        return matches
            .OrderByDescending(m => m.RedundantEstimateBytes)
            .Take(MaxResults)
            .ToList()
            .AsReadOnly();
    }

    // ── Strategies ────────────────────────────────────────────────────────────

    /// <summary>Files sharing a base name once copy/version words are stripped,
    /// within the same directory and extension.</summary>
    private static IEnumerable<NearDuplicateMatch> FindCopyPatterns(
        List<LargeFileRecord> files, CancellationToken ct)
    {
        var baseGroups = new Dictionary<(string Dir, string Base), List<LargeFileRecord>>(
            TupleDirBaseComparer.Instance);

        foreach (var f in files)
        {
            var stem = Path.GetFileNameWithoutExtension(f.FileName);
            var m    = CopyPatternRegex().Match(stem);
            if (!m.Success) continue;

            var key = (f.DirectoryPath, m.Groups[1].Value.Trim() + f.Extension);
            if (!baseGroups.TryGetValue(key, out var group))
                baseGroups[key] = group = [];
            group.Add(f);
        }

        foreach (var group in baseGroups.Values)
        {
            if (ct.IsCancellationRequested) yield break;
            if (group.Count < 2) continue;

            for (int i = 0; i < group.Count; i++)
                for (int j = i + 1; j < group.Count; j++)
                    yield return new NearDuplicateMatch(
                        group[i], group[j],
                        Similarity: 0.90,
                        MatchReason: "Copy or version naming pattern");
        }
    }

    /// <summary>Files with the same normalized name but different extensions
    /// from the same content family (image/video/audio/document/…).</summary>
    private static IEnumerable<NearDuplicateMatch> FindFormatVariants(
        List<LargeFileRecord> files, CancellationToken ct)
    {
        var nameGroups = new Dictionary<string, List<LargeFileRecord>>(StringComparer.Ordinal);

        foreach (var f in files)
        {
            if (string.IsNullOrEmpty(f.Extension)) continue;
            var norm = NormalizeName(f.FileName);
            if (norm.Length == 0) continue;

            if (!nameGroups.TryGetValue(norm, out var group))
                nameGroups[norm] = group = [];
            group.Add(f);
        }

        foreach (var group in nameGroups.Values)
        {
            if (ct.IsCancellationRequested) yield break;
            if (group.Count < 2) continue;

            for (int i = 0; i < group.Count; i++)
            {
                for (int j = i + 1; j < group.Count; j++)
                {
                    var a = group[i];
                    var b = group[j];
                    if (!a.Extension.Equals(b.Extension, StringComparison.OrdinalIgnoreCase) &&
                        AreFormatVariants(a.Extension, b.Extension))
                    {
                        yield return new NearDuplicateMatch(
                            a, b,
                            Similarity: 0.95,
                            MatchReason: $"Same name, different formats ({a.Extension} vs {b.Extension})");
                    }
                }
            }
        }
    }

    /// <summary>Files in the same directory whose normalized names are very
    /// similar and whose sizes are within the proximity ratio.</summary>
    private static IEnumerable<NearDuplicateMatch> FindNameSimilarityMatches(
        List<LargeFileRecord> files, CancellationToken ct)
    {
        var dirBuckets = files
            .GroupBy(f => f.DirectoryPath, StringComparer.OrdinalIgnoreCase);

        foreach (var dirGroup in dirBuckets)
        {
            if (ct.IsCancellationRequested) yield break;

            var bucket = dirGroup.ToList();
            if (bucket.Count > MaxBucketSize)
                bucket = bucket.OrderByDescending(f => f.SizeBytes).Take(MaxBucketSize).ToList();

            for (int i = 0; i < bucket.Count; i++)
            {
                for (int j = i + 1; j < bucket.Count; j++)
                {
                    var a = bucket[i];
                    var b = bucket[j];
                    if (!SizeSimilar(a.SizeBytes, b.SizeBytes)) continue;

                    double sim = NameSimilarity(a.FileName, b.FileName);
                    if (sim >= NameSimilarityThreshold)
                    {
                        yield return new NearDuplicateMatch(
                            a, b, sim,
                            MatchReason: $"Very similar names ({sim:P0} match)");
                    }
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Lowercased stem with copy/version markers and separators removed,
    /// so "Report_FINAL (2).docx" and "report.docx" compare equal.</summary>
    internal static string NormalizeName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        stem = CopyWordRegex().Replace(stem, "");
        stem = NumberSuffixRegex().Replace(stem, "");
        stem = VersionSuffixRegex().Replace(stem, "");
        stem = StatusWordRegex().Replace(stem, "");
        return SeparatorRegex().Replace(stem, " ").Trim();
    }

    /// <summary>Similarity of two file names in [0, 1] using a Levenshtein
    /// ratio over the normalized stems.</summary>
    internal static double NameSimilarity(string nameA, string nameB)
    {
        var a = NormalizeName(nameA);
        var b = NormalizeName(nameB);
        if (a.Length == 0 || b.Length == 0) return 0.0;
        if (a == b) return 1.0;

        int distance = LevenshteinDistance(a, b);
        int maxLen   = Math.Max(a.Length, b.Length);
        return 1.0 - (double)distance / maxLen;
    }

    internal static bool AreFormatVariants(string extA, string extB)
    {
        foreach (var group in s_formatVariantGroups)
        {
            bool hasA = false, hasB = false;
            foreach (var ext in group)
            {
                if (ext.Equals(extA, StringComparison.OrdinalIgnoreCase)) hasA = true;
                if (ext.Equals(extB, StringComparison.OrdinalIgnoreCase)) hasB = true;
            }
            if (hasA && hasB) return true;
        }
        return false;
    }

    private static bool SizeSimilar(long sizeA, long sizeB)
    {
        if (sizeA == 0 || sizeB == 0) return false;
        long larger  = Math.Max(sizeA, sizeB);
        long smaller = Math.Min(sizeA, sizeB);
        return (double)smaller / larger > SizeProximityRatio;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    private static (string, string) PairKey(string pathA, string pathB) =>
        string.CompareOrdinal(pathA, pathB) <= 0 ? (pathA, pathB) : (pathB, pathA);

    /// <summary>Case-insensitive comparer for (directory, base-name) keys.</summary>
    private sealed class TupleDirBaseComparer : IEqualityComparer<(string Dir, string Base)>
    {
        internal static readonly TupleDirBaseComparer Instance = new();

        public bool Equals((string Dir, string Base) x, (string Dir, string Base) y) =>
            string.Equals(x.Dir, y.Dir, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Base, y.Base, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Dir, string Base) obj) =>
            HashCode.Combine(
                obj.Dir.ToUpperInvariant(),
                obj.Base.ToUpperInvariant());
    }
}
