using FluentAssertions;
using Lucid.Services.Storage;
using Xunit;

namespace Lucid.Tests.Storage;

/// <summary>
/// Covers the cache/junk-location classification rules. Classification is
/// informational only (category heatmap) — these tests pin the rules so a
/// future edit cannot silently start classifying user data as Temporary.
/// </summary>
public sealed class StorageCategoryAnalyzerJunkRuleTests
{
    private static StorageCategory Classify(string fullPath) =>
        StorageCategoryAnalyzer.Classify(fullPath, Path.GetExtension(fullPath));

    [Theory]
    [InlineData(@"C:\Users\t\AppData\Local\Microsoft\Windows\INetCache\entry.bin")]
    [InlineData(@"C:\Windows\Prefetch\APP-1234.pf")]
    [InlineData(@"C:\Windows\SoftwareDistribution\Download\update.cab")]
    [InlineData(@"C:\Users\t\AppData\Local\CrashDumps\app.mdmp")]
    [InlineData(@"C:\Users\t\AppData\Roaming\App\Code Cache\js\index.bin")]
    [InlineData(@"C:\Users\t\AppData\Roaming\App\GPUCache\data_1.bin")]
    [InlineData(@"C:\Users\t\AppData\Local\pip\cache\wheels\pkg.whl")]
    [InlineData(@"C:\Users\t\AppData\Local\npm-cache\content-v2\sha512\x.bin")]
    [InlineData(@"C:\Users\t\AppData\Local\Mozilla\Firefox\Profiles\x.default\cache2\entries\abc")]
    public void Classify_KnownCacheLocations_AsTemporary(string fullPath)
    {
        Classify(fullPath).Should().Be(StorageCategory.Temporary);
    }

    [Theory]
    [InlineData(@"C:\Data\report.crash")]
    [InlineData(@"C:\Data\buffer.swp")]
    public void Classify_JunkExtensions_AsTemporary(string fullPath)
    {
        Classify(fullPath).Should().Be(StorageCategory.Temporary);
    }

    [Fact]
    public void Classify_FirefoxProfileDataOutsideCache_IsNotTemporary()
    {
        // Firefox profiles hold bookmarks and credentials; only the cache2
        // subdirectory is cache. Profile data must never classify as Temporary.
        Classify(@"C:\Users\t\AppData\Local\Mozilla\Firefox\Profiles\x.default\places.sqlite")
            .Should().NotBe(StorageCategory.Temporary);
    }

    [Fact]
    public void Classify_UserDocuments_AreUnaffectedByJunkRules()
    {
        Classify(@"C:\Users\t\Documents\thesis.docx")
            .Should().Be(StorageCategory.UserDocuments);
    }
}
