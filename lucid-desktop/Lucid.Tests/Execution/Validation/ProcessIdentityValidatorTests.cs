using FluentAssertions;
using Lucid.Services.Execution.Validation;
using Xunit;

namespace Lucid.Tests.Execution.Validation;

/// <summary>
/// Tests for <see cref="ProcessIdentityValidator"/>.
///
/// These tests use the current process itself as a known-good target since
/// we can always query it without needing elevation.
/// </summary>
public sealed class ProcessIdentityValidatorTests
{
    [Fact]
    public void TryVerify_CurrentProcess_ReturnsVerifiedTarget()
    {
        var pid  = Environment.ProcessId;
        var name = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

        var result = ProcessIdentityValidator.TryVerify(pid, name);

        result.Should().NotBeNull();
        result!.ProcessId.Should().Be(pid);
        result.IsConfirmed.Should().BeTrue(
            because: "we verified the current process against its own name");
    }

    [Fact]
    public void TryVerify_CurrentProcess_WithWrongName_ReturnsMismatch()
    {
        var pid = Environment.ProcessId;

        var result = ProcessIdentityValidator.TryVerify(pid, "definitely-not-this-process-name");

        result.Should().NotBeNull(because: "the process exists — just the name is wrong");
        result!.IsConfirmed.Should().BeFalse(
            because: "the expected name does not match the verified OS name");
    }

    [Fact]
    public void TryVerify_NonExistentPid_ReturnsNull()
    {
        // Use a very high PID unlikely to exist.
        // This is technically racy but PID 9999999 won't exist in practice.
        var result = ProcessIdentityValidator.TryVerify(int.MaxValue, "anything");

        result.Should().BeNull(because: "PID int.MaxValue does not exist");
    }

    [Fact]
    public void TryVerify_NullExpectedName_IsAlwaysConfirmedWhenAlive()
    {
        var pid    = Environment.ProcessId;
        var result = ProcessIdentityValidator.TryVerify(pid, expectedName: null);

        result.Should().NotBeNull();
        result!.IsConfirmed.Should().BeTrue(
            because: "null expected name means 'skip name check'");
    }

    [Fact]
    public void TryVerify_ExeExtensionStripped_MatchesWithoutExtension()
    {
        var pid  = Environment.ProcessId;
        var name = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

        // Pass name WITH .exe extension — validator should strip it
        var result = ProcessIdentityValidator.TryVerify(pid, name + ".exe");

        result.Should().NotBeNull();
        result!.IsConfirmed.Should().BeTrue(
            because: "the .exe extension should be normalised away during comparison");
    }

    [Fact]
    public void CaptureFingerprint_CurrentProcess_ReturnsFingerprint()
    {
        var pid      = Environment.ProcessId;
        var snapshot = ProcessIdentityValidator.CaptureFingerprint(pid);

        snapshot.Should().NotBeNull();
        snapshot!.ProcessId.Should().Be(pid);
        snapshot.CapturedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
