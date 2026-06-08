using FluentAssertions;
using Lucid.Services.Privacy;
using Microsoft.Win32;
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

    [Fact]
    public void TrySetAppPermission_WritesAllowToExistingAppKey()
    {
        using var scope = new RegistryTestScope();
        scope.CreateAppKey("webcam", "Contoso.CameraApp");

        var result = PrivacyPermissionWriter.TrySetAppPermission(
            Registry.CurrentUser,
            scope.StorePath,
            "webcam",
            "Contoso.CameraApp",
            allow: true);

        result.Should().BeTrue();
        scope.ReadPermissionValue("webcam", "Contoso.CameraApp").Should().Be("Allow");
    }

    [Fact]
    public void TrySetAppPermission_WritesDenyForRevocation()
    {
        using var scope = new RegistryTestScope();
        scope.CreateAppKey("microphone", "Contoso.Recorder");

        var result = PrivacyPermissionWriter.TrySetAppPermission(
            Registry.CurrentUser,
            scope.StorePath,
            "microphone",
            "Contoso.Recorder",
            allow: false);

        result.Should().BeTrue();
        scope.ReadPermissionValue("microphone", "Contoso.Recorder").Should().Be("Deny");
    }

    [Fact]
    public void TrySetAppPermission_MissingAppKey_ReturnsFalseAndDoesNotCreateIt()
    {
        using var scope = new RegistryTestScope();
        scope.CreateCapabilityKey("location");

        var result = PrivacyPermissionWriter.TrySetAppPermission(
            Registry.CurrentUser,
            scope.StorePath,
            "location",
            "Missing.App",
            allow: true);

        result.Should().BeFalse();
        scope.AppKeyExists("location", "Missing.App").Should().BeFalse(
            "the writer must not create missing app keys implicitly");
    }

    private sealed class RegistryTestScope : IDisposable
    {
        private readonly string _rootPath;

        public RegistryTestScope()
        {
            _rootPath = $@"Software\Lucid\Tests\PrivacyPermissionWriter\{Guid.NewGuid():N}";
            StorePath = $@"{_rootPath}\ConsentStore";
            Registry.CurrentUser.CreateSubKey(StorePath)?.Dispose();
        }

        public string StorePath { get; }

        public void CreateCapabilityKey(string capabilityName)
        {
            Registry.CurrentUser.CreateSubKey($@"{StorePath}\{capabilityName}")?.Dispose();
        }

        public void CreateAppKey(string capabilityName, string appIdentifier)
        {
            Registry.CurrentUser.CreateSubKey($@"{StorePath}\{capabilityName}\{appIdentifier}")?.Dispose();
        }

        public bool AppKeyExists(string capabilityName, string appIdentifier)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{StorePath}\{capabilityName}\{appIdentifier}");
            return key is not null;
        }

        public string? ReadPermissionValue(string capabilityName, string appIdentifier)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{StorePath}\{capabilityName}\{appIdentifier}");
            return key?.GetValue("Value") as string;
        }

        public void Dispose()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
            }
            catch
            {
            }
        }
    }
}
