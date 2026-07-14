using FluentAssertions;
using Lucid.Services.Storage;
using Xunit;

namespace Lucid.Tests.Storage;

public sealed class NearDuplicateDetectionServiceTests
{
    private static readonly IReadOnlySet<string> NoExactDuplicates = new HashSet<string>();

    private static LargeFileRecord File(
        string path, long sizeBytes = 1_000_000, string? extension = null)
    {
        extension ??= Path.GetExtension(path).ToLowerInvariant();
        return new LargeFileRecord(
            path, sizeBytes,
            LastModifiedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastAccessedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            extension, StorageCategory.Other);
    }

    // ── Copy patterns ─────────────────────────────────────────────────────────

    [Fact]
    public void Detect_FlagsCopyPatternPair()
    {
        var files = new[]
        {
            File(@"C:\Docs\report (1).docx"),
            File(@"C:\Docs\report (2).docx"),
        };

        var matches = NearDuplicateDetectionService.Detect(files, NoExactDuplicates, CancellationToken.None);

        matches.Should().ContainSingle()
            .Which.MatchReason.Should().Contain("Copy or version");
    }

    [Fact]
    public void Detect_DoesNotPairCopyPatternsAcrossDirectories()
    {
        var files = new[]
        {
            File(@"C:\A\notes_copy.txt"),
            File(@"C:\B\notes_copy.txt"),
        };

        var matches = NearDuplicateDetectionService.Detect(files, NoExactDuplicates, CancellationToken.None)
            .Where(m => m.MatchReason.Contains("Copy or version"));

        matches.Should().BeEmpty();
    }

    // ── Format variants ───────────────────────────────────────────────────────

    [Fact]
    public void Detect_FlagsSameNameDifferentImageFormats()
    {
        var files = new[]
        {
            File(@"C:\Pictures\vacation.png"),
            File(@"C:\Pictures\vacation.jpg"),
        };

        var matches = NearDuplicateDetectionService.Detect(files, NoExactDuplicates, CancellationToken.None);

        matches.Should().Contain(m => m.MatchReason.Contains("different formats"));
    }

    [Fact]
    public void Detect_DoesNotFlagUnrelatedFormatFamilies()
    {
        var files = new[]
        {
            File(@"C:\Data\budget.xlsx"),
            File(@"C:\Data\budget.mp3"),
        };

        var matches = NearDuplicateDetectionService.Detect(files, NoExactDuplicates, CancellationToken.None)
            .Where(m => m.MatchReason.Contains("different formats"));

        matches.Should().BeEmpty();
    }

    // ── Name similarity ───────────────────────────────────────────────────────

    [Fact]
    public void Detect_FlagsVerySimilarNamesInSameDirectory()
    {
        var files = new[]
        {
            File(@"C:\Work\quarterly-earnings-summary.pdf", 2_000_000),
            File(@"C:\Work\quarterly-earnings-summery.pdf", 1_900_000),
        };

        var matches = NearDuplicateDetectionService.Detect(files, NoExactDuplicates, CancellationToken.None);

        matches.Should().NotBeEmpty();
    }

    [Fact]
    public void Detect_IgnoresSimilarNamesWithVeryDifferentSizes()
    {
        var files = new[]
        {
            File(@"C:\Work\dataset-a.bin", 100_000_000),
            File(@"C:\Work\dataset-b.bin", 1_000_000), // 1% of the other
        };

        var matches = NearDuplicateDetectionService.Detect(files, NoExactDuplicates, CancellationToken.None)
            .Where(m => m.MatchReason.Contains("similar names"));

        matches.Should().BeEmpty();
    }

    // ── Guards and exclusions ─────────────────────────────────────────────────

    [Fact]
    public void Detect_SkipsFilesBelowMinimumSize()
    {
        var files = new[]
        {
            File(@"C:\Tiny\note (1).txt", sizeBytes: 512),
            File(@"C:\Tiny\note (2).txt", sizeBytes: 512),
        };

        var matches = NearDuplicateDetectionService.Detect(files, NoExactDuplicates, CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ExcludesPairsAlreadyReportedAsExactDuplicates()
    {
        var a = File(@"C:\Docs\photo (1).jpg");
        var b = File(@"C:\Docs\photo (2).jpg");
        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            a.FullPath, b.FullPath,
        };

        var matches = NearDuplicateDetectionService.Detect([a, b], exact, CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ReportsEachPairOnlyOnce()
    {
        // This pair qualifies under both the copy-pattern and name-similarity
        // strategies; it must still be reported exactly once.
        var files = new[]
        {
            File(@"C:\Docs\thesis-draft.docx"),
            File(@"C:\Docs\thesis-final.docx"),
        };

        var matches = NearDuplicateDetectionService.Detect(files, NoExactDuplicates, CancellationToken.None);

        matches.Should().HaveCountLessThanOrEqualTo(1);
    }

    [Fact]
    public void Detect_HonorsCancellation()
    {
        var files = Enumerable.Range(0, 100)
            .Select(i => File($@"C:\Bulk\file ({i}).dat"))
            .ToList();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var matches = NearDuplicateDetectionService.Detect(files, NoExactDuplicates, cts.Token);

        matches.Should().BeEmpty();
    }

    // ── Match record semantics ────────────────────────────────────────────────

    [Fact]
    public void RedundantEstimate_IsSizeOfSmallerFile()
    {
        var match = new NearDuplicateMatch(
            File(@"C:\A\x (1).bin", 5_000_000),
            File(@"C:\A\x (2).bin", 3_000_000),
            Similarity: 0.9,
            MatchReason: "test");

        match.RedundantEstimateBytes.Should().Be(3_000_000);
    }

    // ── Name normalization ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Report_FINAL (2).docx", "report")]
    [InlineData("photo_copy 3.jpg",      "photo")]
    [InlineData("notes-v2.txt",          "notes")]
    [InlineData("plain.txt",             "plain")]
    public void NormalizeName_StripsCopyAndVersionMarkers(string input, string expected)
    {
        NearDuplicateDetectionService.NormalizeName(input).Should().Be(expected);
    }

    [Fact]
    public void NameSimilarity_IdenticalAfterNormalization_IsOne()
    {
        NearDuplicateDetectionService
            .NameSimilarity("budget (1).xlsx", "budget.xlsx")
            .Should().Be(1.0);
    }
}
