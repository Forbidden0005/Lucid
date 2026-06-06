using FluentAssertions;
using Lucid.Services.Privacy;
using Xunit;

namespace Lucid.Tests.Privacy;

public sealed class PrivacyPermissionWriterTests
{
    [Theory]
    [InlineData("webcam")]
    [InlineData("microphone")]
    [InlineData("userAccountInformation")]
    [InlineData("broadFileSystemAccess")]
    public void IsValidCapabilityName_AllowsKnownRegistryKeyShape(string capabilityName)
    {
        PrivacyPermissionWriter.IsValidCapabilityName(capabilityName)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("webcam\\Injected")]
    [InlineData("webcam/Injected")]
    [InlineData("webcam:Injected")]
    public void IsValidCapabilityName_RejectsEmptyOrPathLikeValues(string? capabilityName)
    {
        PrivacyPermissionWriter.IsValidCapabilityName(capabilityName)
            .Should().BeFalse();
    }
}
