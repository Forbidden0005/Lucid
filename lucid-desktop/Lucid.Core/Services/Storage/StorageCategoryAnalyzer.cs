namespace Lucid.Services.Storage;

/// <summary>
/// Classifies files into StorageCategory based on path substrings and extensions.
///
/// Order of precedence: path-based rules first, then extension-based.
/// All comparisons are case-insensitive.
/// </summary>
internal static class StorageCategoryAnalyzer
{
    // ── Path-based classifications (checked first) ────────────────────────────

    private static readonly (string PathFragment, StorageCategory Category)[] s_pathRules =
    [
        // Specific cache locations must precede the general \Windows\ and
        // \AppData\ rules — first match wins.
        (@"\Microsoft\Windows\INetCache\",   StorageCategory.Temporary),
        (@"\SoftwareDistribution\Download\", StorageCategory.Temporary),
        (@"\Windows\Prefetch\",              StorageCategory.Temporary),

        (@"\Windows\",           StorageCategory.System),
        (@"\Program Files\",     StorageCategory.System),
        (@"\Program Files (x86)\", StorageCategory.System),
        (@"\ProgramData\",       StorageCategory.System),
        (@"\Downloads\",         StorageCategory.Downloads),
        (@"\Desktop\",           StorageCategory.UserDocuments),
        (@"\Documents\",         StorageCategory.UserDocuments),
        (@"\Pictures\",          StorageCategory.Images),
        (@"\Videos\",            StorageCategory.Video),
        (@"\Music\",             StorageCategory.Audio),
        (@"\Temp\",              StorageCategory.Temporary),
        (@"\tmp\",               StorageCategory.Temporary),
        (@"\AppData\Local\Temp", StorageCategory.Temporary),
        // Known browser / package-manager cache locations. These are
        // regenerable by the apps that own them, so they classify as Temporary
        // for the category heatmap (classification only — no cleanup implied).
        (@"\CrashDumps\",                               StorageCategory.Temporary),
        (@"\Crash Reports\",                            StorageCategory.Temporary),
        (@"\Code Cache\",                               StorageCategory.Temporary),
        (@"\GPUCache\",                                 StorageCategory.Temporary),
        (@"\GrShaderCache\",                            StorageCategory.Temporary),
        (@"\ShaderCache\",                              StorageCategory.Temporary),
        (@"\blob_storage\",                             StorageCategory.Temporary),
        (@"\pip\cache\",                                StorageCategory.Temporary),
        (@"\npm-cache\",                                StorageCategory.Temporary),
        (@"\yarn\Cache\",                               StorageCategory.Temporary),
        (@"\AppData\Local\Google\Chrome\User Data\Default\Cache\", StorageCategory.Temporary),
        (@"\cache2\",                                   StorageCategory.Temporary), // Firefox cache dir
        (@"\CachedData\",                               StorageCategory.Temporary), // VS Code et al.
        (@"\node_modules\",      StorageCategory.Development),
        (@"\.git\",              StorageCategory.Development),
        (@"\source\repos\",      StorageCategory.Development),
        (@"\Projects\",          StorageCategory.Development),
        (@"\Backup\",            StorageCategory.Backup),
        (@"\Backups\",           StorageCategory.Backup),
    ];

    // ── Extension-based classifications (checked after path rules) ────────────

    private static readonly Dictionary<string, StorageCategory> s_extMap =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        [".jpg"] = StorageCategory.Images, [".jpeg"] = StorageCategory.Images,
        [".png"]  = StorageCategory.Images, [".gif"] = StorageCategory.Images,
        [".bmp"]  = StorageCategory.Images, [".tiff"] = StorageCategory.Images,
        [".webp"] = StorageCategory.Images, [".heic"] = StorageCategory.Images,
        [".raw"]  = StorageCategory.Images, [".cr2"] = StorageCategory.Images,

        // Video
        [".mp4"] = StorageCategory.Video,  [".mkv"] = StorageCategory.Video,
        [".avi"] = StorageCategory.Video,  [".mov"] = StorageCategory.Video,
        [".wmv"] = StorageCategory.Video,  [".flv"] = StorageCategory.Video,
        [".webm"] = StorageCategory.Video, [".m4v"] = StorageCategory.Video,

        // Audio
        [".mp3"]  = StorageCategory.Audio, [".flac"] = StorageCategory.Audio,
        [".wav"]  = StorageCategory.Audio, [".aac"]  = StorageCategory.Audio,
        [".ogg"]  = StorageCategory.Audio, [".m4a"]  = StorageCategory.Audio,
        [".wma"]  = StorageCategory.Audio,

        // Installers / disk images
        [".iso"]  = StorageCategory.Installers, [".img"] = StorageCategory.Installers,
        [".exe"]  = StorageCategory.Installers, [".msi"] = StorageCategory.Installers,
        [".msix"] = StorageCategory.Installers, [".appx"] = StorageCategory.Installers,
        [".dmg"]  = StorageCategory.Installers,

        // Archives (often abandoned downloads)
        [".zip"] = StorageCategory.Downloads, [".rar"] = StorageCategory.Downloads,
        [".7z"]  = StorageCategory.Downloads, [".tar"] = StorageCategory.Downloads,
        [".gz"]  = StorageCategory.Downloads, [".bz2"] = StorageCategory.Downloads,

        // Temp / junk
        [".tmp"]  = StorageCategory.Temporary, [".temp"]  = StorageCategory.Temporary,
        [".log"]  = StorageCategory.Temporary, [".dmp"]   = StorageCategory.Temporary,
        [".etl"]  = StorageCategory.Temporary, [".crash"] = StorageCategory.Temporary,
        [".swp"]  = StorageCategory.Temporary,

        // Development
        [".pdb"]  = StorageCategory.Development, [".obj"] = StorageCategory.Development,
        [".lib"]  = StorageCategory.Development, [".a"]   = StorageCategory.Development,

        // Backup
        [".bak"] = StorageCategory.Backup, [".bkp"] = StorageCategory.Backup,
        [".old"] = StorageCategory.Backup,

        // Documents
        [".pdf"]  = StorageCategory.UserDocuments,
        [".docx"] = StorageCategory.UserDocuments, [".doc"] = StorageCategory.UserDocuments,
        [".xlsx"] = StorageCategory.UserDocuments, [".xls"] = StorageCategory.UserDocuments,
        [".pptx"] = StorageCategory.UserDocuments, [".ppt"] = StorageCategory.UserDocuments,
        [".txt"]  = StorageCategory.UserDocuments,
    };

    /// <summary>Classifies a file into a broad storage category.</summary>
    internal static StorageCategory Classify(FileInfo file) =>
        Classify(file.FullName, file.Extension);

    /// <summary>Pure path/extension classification (unit-testable).</summary>
    internal static StorageCategory Classify(string fullPath, string extension)
    {
        // Path rules take precedence
        foreach (var (fragment, category) in s_pathRules)
        {
            if (fullPath.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return category;
        }

        // Extension fallback
        if (!string.IsNullOrEmpty(extension) &&
            s_extMap.TryGetValue(extension, out var extCategory))
            return extCategory;

        return StorageCategory.Other;
    }

    /// <summary>
    /// Aggregates a flat list of scanned files into per-category snapshots.
    /// </summary>
    internal static IReadOnlyList<StorageCategorySnapshot> Aggregate(
        IEnumerable<LargeFileRecord> files)
    {
        var buckets = new Dictionary<StorageCategory, (long Bytes, int Count)>();

        foreach (var f in files)
        {
            if (!buckets.TryGetValue(f.Category, out var current))
                current = (0, 0);
            buckets[f.Category] = (current.Bytes + f.SizeBytes, current.Count + 1);
        }

        return buckets
            .Select(kv => new StorageCategorySnapshot(kv.Key, kv.Value.Bytes, kv.Value.Count))
            .OrderByDescending(s => s.TotalBytes)
            .ToList()
            .AsReadOnly();
    }
}
