using FluentAssertions;
using Lucid.Services.Execution.Validation;
using Xunit;

namespace Lucid.Tests.Execution;

/// <summary>
/// Guards <see cref="DuplicateProtectionPolicy"/> — the single rule shared by
/// the delete executor (which refuses protected groups) and the Storage UI
/// (which routes them to the "review manually" section instead of showing a
/// delete button the executor would reject). A group is protected when the keep
/// candidate OR any delete candidate lives in a guarded location.
/// </summary>
public sealed class DuplicateProtectionPolicyTests
{
    [Fact]
    public void UserFilesOnly_AreActionable()
    {
        var reason = DuplicateProtectionPolicy.FindBlockedReason(
            @"C:\Users\tyler\Documents\report.pdf",
            [@"C:\Users\tyler\Desktop\report copy.pdf"]);

        reason.Should().BeNull();
        DuplicateProtectionPolicy.IsProtected(
            @"C:\Users\tyler\Documents\report.pdf",
            [@"C:\Users\tyler\Desktop\report copy.pdf"]).Should().BeFalse();
    }

    [Fact]
    public void ProgramDataCopy_IsProtected()
    {
        // The exact field case: a BlueStacks VM disk duplicated under ProgramData.
        var reason = DuplicateProtectionPolicy.FindBlockedReason(
            @"C:\ProgramData\BlueStacks_nxt\Engine\Pie64\Data.vhdx",
            [@"C:\ProgramData\BlueStacks_nxt\Engine\Nougat64\Data.vhdx"]);

        reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ProtectedKeepCandidate_TaintsWholeGroup()
    {
        // Keep candidate is protected; the delete candidate is safe — the whole
        // group is still refused, because a protected keep path means the
        // grouping itself can't be trusted.
        DuplicateProtectionPolicy.IsProtected(
            @"C:\Windows\System32\driver.sys",
            [@"C:\Users\tyler\Downloads\driver.sys"]).Should().BeTrue();
    }

    [Fact]
    public void ProtectedDeleteCandidate_TaintsWholeGroup()
    {
        // Keep candidate is safe, but staging the protected delete candidate
        // would hit the guard — so the group is protected.
        DuplicateProtectionPolicy.IsProtected(
            @"C:\Users\tyler\Downloads\setup.exe",
            [@"C:\Program Files\App\setup.exe"]).Should().BeTrue();
    }

    [Fact]
    public void AbsentKeepPath_ChecksOnlyDeleteList()
    {
        DuplicateProtectionPolicy.FindBlockedReason(
            null, [@"C:\Users\tyler\Downloads\a.bin"]).Should().BeNull();

        DuplicateProtectionPolicy.FindBlockedReason(
            null, [@"C:\Windows\System32\a.bin"]).Should().NotBeNull();
    }
}
