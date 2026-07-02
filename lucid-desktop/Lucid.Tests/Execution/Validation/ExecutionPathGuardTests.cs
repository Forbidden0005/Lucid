using FluentAssertions;
using Lucid.Services.Execution.Validation;
using Xunit;

namespace Lucid.Tests.Execution.Validation;

/// <summary>
/// Covers the shared destructive-path denylist. These rules protect Windows
/// system directories, driver stores, and credential stores from every
/// file-touching executor, so both the block and allow sides must hold.
/// </summary>
public sealed class ExecutionPathGuardTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32\config\SYSTEM")]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"C:\Program Files\App\big.dat")]
    [InlineData(@"C:\Program Files (x86)\App\big.dat")]
    [InlineData(@"C:\ProgramData\Vendor\cache.bin")]
    [InlineData(@"C:\Windows.old\stale.dll")]
    [InlineData(@"D:\backup\drivers\netdriver.sys")]
    [InlineData(@"C:\Users\u\AppData\Roaming\Microsoft\Credentials\blob")]
    [InlineData(@"C:\Users\u\AppData\Local\Microsoft\Vault\v.dat")]
    [InlineData(@"C:\Users\u\AppData\Roaming\Mozilla\Firefox\Profiles\x\places.sqlite")]
    [InlineData(@"C:\Users\u\AppData\Local\Google\Chrome\User Data\Default\Login Data")]
    public void CheckDestructiveTarget_ProtectedPath_ReturnsBlockReason(string path)
    {
        var reason = ExecutionPathGuard.CheckDestructiveTarget(path);

        reason.Should().NotBeNull("protected locations must never be mutated");
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:")]
    public void CheckDestructiveTarget_DriveRoot_IsBlocked(string path)
    {
        ExecutionPathGuard.CheckDestructiveTarget(path).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CheckDestructiveTarget_EmptyPath_IsBlocked(string path)
    {
        ExecutionPathGuard.CheckDestructiveTarget(path).Should().NotBeNull();
    }

    [Fact]
    public void CheckDestructiveTarget_IsCaseInsensitive_AndNormalisesForwardSlashes()
    {
        ExecutionPathGuard.CheckDestructiveTarget(@"c:/WINDOWS/system32/kernel32.dll")
            .Should().NotBeNull("matching must survive casing and separator variations");
    }

    [Theory]
    [InlineData(@"C:\Users\u\Downloads\movie.mkv")]
    [InlineData(@"C:\Users\u\Documents\old-report.pdf")]
    [InlineData(@"D:\media\archive\backup.zip")]
    public void CheckDestructiveTarget_OrdinaryUserPath_IsAllowed(string path)
    {
        ExecutionPathGuard.CheckDestructiveTarget(path)
            .Should().BeNull("legitimate cleanup targets must not be blocked");
    }
}
