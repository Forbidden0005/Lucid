using FluentAssertions;
using Lucid.Services.LlmChat;
using Lucid.Services.Trust.EndpointValidation;
using Xunit;

namespace Lucid.Tests.LlmChat;

/// <summary>
/// Verifies that the concrete Ollama client enforces the same local-only
/// endpoint policy as the standalone validator.
/// </summary>
public sealed class OllamaClientEndpointPolicyTests
{
    [Theory]
    [InlineData("http://localhost:11434", EndpointTrustClassification.Local, "http://localhost:11434")]
    [InlineData("https://LOCALHOST:11434", EndpointTrustClassification.Local, "https://LOCALHOST:11434")]
    [InlineData("http://[::1]:11434", EndpointTrustClassification.Local, "http://[::1]:11434")]
    public void Constructor_LocalEndpoint_PreservesConfiguredEndpoint(
        string endpoint,
        EndpointTrustClassification expectedClassification,
        string expectedOriginalUrl)
    {
        using var client = new OllamaClient(endpoint, "llama3.2:3b");

        client.EndpointValidation.IsAllowed.Should().BeTrue();
        client.EndpointValidation.Classification.Should().Be(expectedClassification);
        client.EndpointValidation.OriginalUrl.Should().Be(expectedOriginalUrl);
    }

    [Theory]
    [InlineData("http://192.168.1.20:11434", EndpointTrustClassification.PrivateLan)]
    [InlineData("http://10.0.0.5:11434", EndpointTrustClassification.PrivateLan)]
    [InlineData("https://api.openai.com/v1", EndpointTrustClassification.Remote)]
    [InlineData("http://example.com:11434", EndpointTrustClassification.Remote)]
    public async Task Constructor_NonLocalEndpoint_FallsBackToDefaultLocalhost(
        string endpoint,
        EndpointTrustClassification expectedClassification)
    {
        using var client = new OllamaClient(endpoint, "llama3.2:3b");

        client.EndpointValidation.IsAllowed.Should().BeFalse();
        client.EndpointValidation.Classification.Should().Be(expectedClassification);

        _ = await client.IsAvailableAsync();
    }

    [Fact]
    public void Constructor_EmptyEndpoint_UsesDefaultAndMarksValidationDenied()
    {
        using var client = new OllamaClient("   ", "llama3.2:3b");

        client.EndpointValidation.IsAllowed.Should().BeFalse();
        client.EndpointValidation.Classification.Should().Be(EndpointTrustClassification.Unknown);
        client.EndpointValidation.Reason.Should().Contain("empty");
    }

    [Fact]
    public void Constructor_WhitespaceModel_FallsBackToDefaultModel()
    {
        using var client = new OllamaClient("http://localhost:11434", "   ");

        client.ModelName.Should().Be(OllamaClient.DefaultModelName);
    }

    [Fact]
    public void Constructor_TrimmedRemoteEndpoint_IsStillRejected()
    {
        using var client = new OllamaClient("  https://api.openai.com/v1  ", "llama3.2:3b");

        client.EndpointValidation.IsAllowed.Should().BeFalse();
        client.EndpointValidation.Classification.Should().Be(EndpointTrustClassification.Remote);
        client.EndpointValidation.OriginalUrl.Should().Be("  https://api.openai.com/v1  ");
    }
}
