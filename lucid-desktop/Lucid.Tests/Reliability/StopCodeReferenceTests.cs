using FluentAssertions;
using Lucid.Services.Reliability;
using Xunit;

namespace Lucid.Tests.Reliability;

/// <summary>
/// The stop-code table exists so that explaining a blue screen does not depend
/// on a 3-billion parameter model happening to remember what 0x133 means. These
/// tests cover the risky part: event text is inconsistent about the width and
/// case of the code, so lookup must not be string matching.
/// </summary>
public sealed class StopCodeReferenceTests
{
    [Theory]
    [InlineData("0x00000133")]
    [InlineData("0x133")]
    [InlineData("0X0133")]
    [InlineData("0x0000000000000133")]
    [InlineData("133")]
    public void Describe_MatchesAnyRenderingOfTheSameCode(string rendering)
        => StopCodeReference.Describe(rendering)!.Name.Should().Be("DPC_WATCHDOG_VIOLATION");

    [Fact]
    public void Describe_LowercaseHexDigits_StillMatch()
        => StopCodeReference.Describe("0x0000009f")!.Name.Should().Be("DRIVER_POWER_STATE_FAILURE");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0x")]
    [InlineData("not-a-code")]
    public void Describe_Unparseable_ReturnsNull(string? input)
        => StopCodeReference.Describe(input).Should().BeNull();

    [Fact]
    public void Describe_ACodeNotInTheTable_ReturnsNull()
        => StopCodeReference.Describe("0x00000ABC").Should().BeNull();

    [Fact]
    public void Parse_ReadsTheNumericValue()
    {
        StopCodeReference.Parse("0x0000009F").Should().Be(0x9F);
        StopCodeReference.Parse("0x124").Should().Be(0x124);
    }

    // ── Content quality ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("0x00000124", "WHEA_UNCORRECTABLE_ERROR")]
    [InlineData("0x00000101", "CLOCK_WATCHDOG_TIMEOUT")]
    [InlineData("0x0000007A", "KERNEL_DATA_INPAGE_ERROR")]
    [InlineData("0x00000117", "VIDEO_TDR_TIMEOUT")]
    [InlineData("0x0000001A", "MEMORY_MANAGEMENT")]
    public void Describe_KnownCodes_CarryTheirWindowsName(string code, string expectedName)
        => StopCodeReference.Describe(code)!.Name.Should().Be(expectedName);

    [Fact]
    public void EveryEntryLeadsWithItsNameAndOffersSomethingToDo()
    {
        // A code whose explanation does not name it, or that offers no next step,
        // is worse than no entry — it reads as authoritative while being useless.
        var codes = new[]
        {
            "0x0000000A", "0x0000001A", "0x0000001E", "0x00000024", "0x0000003B",
            "0x00000050", "0x0000007A", "0x0000007E", "0x0000009C", "0x0000009F",
            "0x000000BE", "0x000000C5", "0x000000EF", "0x000000F4", "0x000000FC",
            "0x00000101", "0x00000109", "0x00000117", "0x00000124", "0x00000133",
            "0x00000139", "0x0000013A", "0x00000144", "0x00000154", "0x0000012B",
        };

        foreach (var code in codes)
        {
            var info = StopCodeReference.Describe(code);

            info.Should().NotBeNull($"{code} should be in the table");
            info!.Meaning.Should().Contain(info.Name, $"{code}'s explanation should name the code");
            info.CommonCauses.Should().NotBeNullOrWhiteSpace();
            info.Checks.Should().NotBeEmpty($"{code} should suggest a next step");
        }
    }

    [Fact]
    public void HardwareCodes_PointAtHardwareRatherThanAtWindows()
    {
        StopCodeReference.Describe("0x00000124")!
            .Meaning.Should().Contain("hardware");

        StopCodeReference.Describe("0x0000009C")!
            .Meaning.Should().Contain("CPU");
    }
}
