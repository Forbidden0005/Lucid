using FluentAssertions;
using Lucid.Services.Trust.EndpointValidation;
using Xunit;

namespace Lucid.Tests.Trust.EndpointValidation;

/// <summary>
/// Verifies the local-only enforcement logic in <see cref="LocalEndpointValidator"/>.
/// These tests encode the security invariants — a regression here means data
/// could leave the machine.
/// </summary>
public sealed class LocalEndpointValidatorTests
{
    // ── Should allow ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("https://localhost:11434")]
    [InlineData("http://localhost:11434/api")]
    [InlineData("http://127.0.0.1:8080")]
    public void Validate_LocalAddresses_AreAllowed(string url)
    {
        var result = LocalEndpointValidator.Validate(url);

        result.IsAllowed.Should().BeTrue(because: $"'{url}' is a local address");
        result.Classification.Should().Be(EndpointTrustClassification.Local);
    }

    // ── Should deny ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://192.168.1.100:11434")]
    [InlineData("http://10.0.0.1:11434")]
    [InlineData("http://172.20.0.1:11434")]
    public void Validate_PrivateLanAddresses_AreDenied(string url)
    {
        var result = LocalEndpointValidator.Validate(url);

        result.IsAllowed.Should().BeFalse(because: $"'{url}' is a LAN address — data would leave the machine");
        result.Classification.Should().Be(EndpointTrustClassification.PrivateLan);
    }

    [Theory]
    [InlineData("http://8.8.8.8:11434")]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("http://ollama.remote.example.com:11434")]
    public void Validate_RemoteAddresses_AreDenied(string url)
    {
        var result = LocalEndpointValidator.Validate(url);

        result.IsAllowed.Should().BeFalse(because: $"'{url}' is a remote address");
        result.Classification.Should().Be(EndpointTrustClassification.Remote);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrNull_IsUnknownAndDenied(string? url)
    {
        var result = LocalEndpointValidator.Validate(url);

        result.IsAllowed.Should().BeFalse();
        result.Classification.Should().Be(EndpointTrustClassification.Unknown);
    }

    [Fact]
    public void Validate_InvalidUri_IsUnknownAndDenied()
    {
        var result = LocalEndpointValidator.Validate("not-a-url");

        result.IsAllowed.Should().BeFalse();
        result.Classification.Should().Be(EndpointTrustClassification.Unknown);
    }

    [Fact]
    public void Validate_FtpScheme_IsUnknownAndDenied()
    {
        var result = LocalEndpointValidator.Validate("ftp://localhost:21");

        result.IsAllowed.Should().BeFalse();
        result.Classification.Should().Be(EndpointTrustClassification.Unknown);
    }

    // ── EnforceOrFallback ─────────────────────────────────────────────────────

    [Fact]
    public void EnforceOrFallback_LocalUrl_ReturnsOriginal()
    {
        var url    = "http://127.0.0.1:11434";
        var result = LocalEndpointValidator.EnforceOrFallback(url);

        result.Should().Be(url);
    }

    [Fact]
    public void EnforceOrFallback_RemoteUrl_ReturnsFallback()
    {
        var result = LocalEndpointValidator.EnforceOrFallback(
            "https://api.openai.com/v1",
            "http://localhost:11434");

        result.Should().Be("http://localhost:11434");
    }

    [Fact]
    public void EnforceOrFallback_NullUrl_ReturnsFallback()
    {
        var result = LocalEndpointValidator.EnforceOrFallback(null, "http://localhost:11434");

        result.Should().Be("http://localhost:11434");
    }

    // ── Denial reason message ─────────────────────────────────────────────────

    [Fact]
    public void Validate_RemoteAddress_ReasonContainsGuidance()
    {
        var result = LocalEndpointValidator.Validate("http://8.8.8.8:11434");

        result.Reason.Should().Contain("localhost",
            because: "the denial message should guide the user to use localhost");
    }
}
