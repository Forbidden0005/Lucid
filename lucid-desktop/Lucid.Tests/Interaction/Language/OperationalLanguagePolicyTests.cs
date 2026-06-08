using FluentAssertions;
using Lucid.Services.Interaction.Language;
using Xunit;

namespace Lucid.Tests.Interaction.Language;

public sealed class OperationalLanguagePolicyTests
{
    [Theory]
    [InlineData("This process shows unusual behavior worth reviewing.", true)]
    [InlineData("This file may indicate an unusual persistence pattern.", true)]
    [InlineData("This process is malicious.", false)]
    [InlineData("Threat detected in a startup entry.", false)]
    [InlineData("Your PC is in danger.", false)]
    public void IsCompliant_ReflectsPolicyTerms(string text, bool expected)
    {
        OperationalLanguagePolicy.IsCompliant(text).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_RewritesFearBasedTerms()
    {
        var sanitized = OperationalLanguagePolicy.Sanitize(
            "Threat detected. This program may be malicious or dangerous.");

        sanitized.Should().NotContain("Threat detected");
        sanitized.Should().NotContain("malicious");
        sanitized.Should().NotContain("dangerous");
        OperationalLanguagePolicy.IsCompliant(sanitized).Should().BeTrue();
    }

    [Fact]
    public void FindViolations_ReturnsMatchedTerms()
    {
        var violations = OperationalLanguagePolicy.FindViolations(
            "This process may be malicious and your PC is in danger.");

        violations.Should().Contain("malicious");
        violations.Should().Contain("in danger");
    }
}
