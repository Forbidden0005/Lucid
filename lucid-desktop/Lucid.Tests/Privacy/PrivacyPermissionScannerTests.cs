using FluentAssertions;
using Lucid.Services.Privacy;
using Microsoft.Win32;
using Xunit;

namespace Lucid.Tests.Privacy;

public sealed class PrivacyPermissionScannerTests
{
    [Fact]
    public void Scan_UsesFallbackStoreWhenPrimaryIsMissing()
    {
        using var primary = new RegistryStoreScope("Primary", createStore: false);
        using var fallback = new RegistryStoreScope("Fallback");

        fallback.CreateCapability("webcam", globalValue: "Allow");
        fallback.CreateApp(
            "webcam",
            "Contoso.Camera_8wekyb3d8bbwe",
            value: "Allow");

        var results = PrivacyPermissionScanner.Scan(
            Registry.CurrentUser,
            primary.StorePath,
            Registry.CurrentUser,
            fallback.StorePath);

        results.Should().ContainSingle();
        results[0].PermissionType.Should().Be("webcam");
        results[0].Apps.Should().ContainSingle(a => a.AppDisplayName == "Camera");
    }

    [Fact]
    public void Scan_ParsesCapabilitiesAndAppsWithStableOrdering()
    {
        using var scope = new RegistryStoreScope("Ordering");

        scope.CreateCapability("microphone", globalValue: "Allow");
        scope.CreateApp(
            "microphone",
            @"NonPackaged\C:#Program Files#Zoom#bin#Zoom.exe",
            value: "Allow");
        scope.CreateApp(
            "microphone",
            "Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe",
            value: "Deny",
            lastUsed: DateTimeOffset.UtcNow.AddHours(-3));

        scope.CreateCapability("location", globalValue: "Deny");
        scope.CreateApp(
            "location",
            "Microsoft.WindowsMaps_8wekyb3d8bbwe",
            value: "Allow");

        var results = PrivacyPermissionScanner.Scan(
            Registry.CurrentUser,
            scope.StorePath,
            fallbackRoot: null,
            fallbackConsentStorePath: scope.StorePath);

        results.Select(r => r.PermissionType)
               .Should().Equal(new[] { "microphone", "location" },
                   because: "categories should be sorted by app count descending");

        var microphone = results[0];
        microphone.DisplayName.Should().Be("Microphone");
        microphone.GloballyEnabled.Should().BeTrue();
        microphone.Apps.Select(a => a.AppDisplayName)
                  .Should().Equal(new[] { "Zoom", "WindowsSoundRecorder" },
                      because: "allowed apps should surface before denied apps");

        microphone.Apps[0].IsPackaged.Should().BeFalse();
        microphone.Apps[0].LastUsed.Should().BeNull();

        microphone.Apps[1].IsPackaged.Should().BeTrue();
        microphone.Apps[1].LastUsed.Should().NotBeNull();

        var location = results[1];
        location.DisplayName.Should().Be("Location");
        location.GloballyEnabled.Should().BeFalse();
    }

    [Fact]
    public void Scan_SkipsCapabilitiesWithoutAppEntries()
    {
        using var scope = new RegistryStoreScope("Empty");

        scope.CreateCapability("webcam", globalValue: "Allow");

        var results = PrivacyPermissionScanner.Scan(
            Registry.CurrentUser,
            scope.StorePath,
            fallbackRoot: null,
            fallbackConsentStorePath: scope.StorePath);

        results.Should().BeEmpty(
            "capabilities without app entries should not surface in the page model");
    }

    private sealed class RegistryStoreScope : IDisposable
    {
        private readonly string _rootPath;

        public RegistryStoreScope(string name, bool createStore = true)
        {
            _rootPath = $@"Software\Lucid\Tests\PrivacyPermissionScanner\{name}\{Guid.NewGuid():N}";
            StorePath = $@"{_rootPath}\ConsentStore";

            if (createStore)
                Registry.CurrentUser.CreateSubKey(StorePath)?.Dispose();
        }

        public string StorePath { get; }

        public void CreateCapability(string capabilityName, string globalValue)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{StorePath}\{capabilityName}");
            key?.SetValue("Value", globalValue, RegistryValueKind.String);
        }

        public void CreateApp(
            string capabilityName,
            string appIdentifier,
            string value,
            DateTimeOffset? lastUsed = null)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{StorePath}\{capabilityName}\{appIdentifier}");
            key?.SetValue("Value", value, RegistryValueKind.String);

            if (lastUsed.HasValue)
                key?.SetValue("LastUsedTimeStop", lastUsed.Value.ToFileTime(), RegistryValueKind.QWord);
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
