namespace ExplainMyPC.Services.Storage;

/// <summary>
/// Immutable record describing a single large file found during a storage scan.
/// </summary>
public sealed record LargeFileRecord(
    string         FullPath,
    long           SizeBytes,
    DateTime       LastModifiedUtc,
    DateTime       LastAccessedUtc,
    string         Extension,
    StorageCategory Category)
{
    public string FileName      => Path.GetFileName(FullPath);
    public string DirectoryPath => Path.GetDirectoryName(FullPath) ?? string.Empty;
    public string SizeFormatted => StorageFormatHelper.FormatBytes(SizeBytes);

    public string AgeDescription
    {
        get
        {
            var age = DateTime.UtcNow - LastModifiedUtc;
            return age.TotalDays switch
            {
                < 1   => "today",
                < 7   => $"{(int)age.TotalDays}d ago",
                < 30  => $"{(int)(age.TotalDays / 7)}w ago",
                < 365 => $"{(int)(age.TotalDays / 30)}mo ago",
                _     => $"{(int)(age.TotalDays / 365)}y ago",
            };
        }
    }
}

/// <summary>
/// A group of duplicate files identified by content hash.
/// All files share identical byte content; the first entry (earliest LastModifiedUtc)
/// is the keep candidate; the rest are deletion candidates.
/// </summary>
public sealed record DuplicateFileGroup(
    string                         Hash,
    IReadOnlyList<LargeFileRecord> Files)
{
    public int    Count           => Files.Count;
    public long   TotalSizeBytes  => Files.Sum(f => f.SizeBytes);
    public long   WasteSizeBytes  => TotalSizeBytes - (Files.Count > 0 ? Files[0].SizeBytes : 0);
    public string WasteFormatted  => StorageFormatHelper.FormatBytes(WasteSizeBytes);
    public string TotalFormatted  => StorageFormatHelper.FormatBytes(TotalSizeBytes);

    public LargeFileRecord                 KeepCandidate    => Files[0];
    public IReadOnlyList<LargeFileRecord>  DeleteCandidates => Files.Skip(1).ToList();
}

/// <summary>Broad storage categories for heatmap classification.</summary>
public enum StorageCategory
{
    System,
    UserDocuments,
    Images,
    Video,
    Audio,
    Downloads,
    Installers,
    Temporary,
    Development,
    Backup,
    Other,
}

/// <summary>Per-category usage summary for the storage heatmap.</summary>
public sealed record StorageCategorySnapshot(
    StorageCategory Category,
    long            TotalBytes,
    int             FileCount)
{
    public string Label         => Category.ToString();
    public string SizeFormatted => StorageFormatHelper.FormatBytes(TotalBytes);
}

/// <summary>Complete result of a storage analysis scan.</summary>
public sealed record StorageAnalysisResult(
    string                                 ScanRoot,
    DateTimeOffset                         CompletedAt,
    TimeSpan                               Duration,
    long                                   TotalBytesScanned,
    int                                    TotalFilesScanned,
    IReadOnlyList<LargeFileRecord>         LargeFiles,
    IReadOnlyList<DuplicateFileGroup>      DuplicateGroups,
    IReadOnlyList<StorageCategorySnapshot> CategoryBreakdown,
    bool                                   WasCancelled)
{
    public long   TotalWasteBytes     => DuplicateGroups.Sum(g => g.WasteSizeBytes);
    public string WasteFormatted      => StorageFormatHelper.FormatBytes(TotalWasteBytes);
    public long   LargeFilesBytes     => LargeFiles.Sum(f => f.SizeBytes);
    public string LargeFilesFormatted => StorageFormatHelper.FormatBytes(LargeFilesBytes);
}

/// <summary>Shared byte-formatting helper.</summary>
public static class StorageFormatHelper
{
    public static string FormatBytes(long bytes) => bytes switch
    {
        < 0             => "0 B",
        < 1_024         => $"{bytes} B",
        < 1_048_576     => $"{bytes / 1_024.0:F1} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
        _               => $"{bytes / 1_073_741_824.0:F2} GB",
    };
}
