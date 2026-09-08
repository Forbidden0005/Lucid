using FluentAssertions;
using Lucid.Services.Chat;
using Xunit;

namespace Lucid.Tests.Chat;

/// <summary>
/// Session titles are generated from whatever the user typed first, so the
/// generator has to cope with real input: pasted log output, lowercase
/// fragments, and very long sentences.
/// </summary>
public sealed class ChatSessionTitleGeneratorTests
{
    // ── FromFirstMessage ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    public void FromFirstMessage_NoUsableText_ReturnsDefault(string? input)
        => ChatSessionTitleGenerator.FromFirstMessage(input)
               .Should().Be(ChatSessionTitleGenerator.DefaultTitle);

    [Fact]
    public void FromFirstMessage_ShortSentence_IsUsedAsIs()
        => ChatSessionTitleGenerator.FromFirstMessage("Why is my disk full?")
               .Should().Be("Why is my disk full?");

    [Fact]
    public void FromFirstMessage_LowercaseInput_IsCapitalised()
        => ChatSessionTitleGenerator.FromFirstMessage("my pc gets loud")
               .Should().Be("My pc gets loud");

    [Fact]
    public void FromFirstMessage_TrailingPeriod_IsDropped()
        => ChatSessionTitleGenerator.FromFirstMessage("The fans keep spinning up.")
               .Should().Be("The fans keep spinning up");

    [Fact]
    public void FromFirstMessage_QuestionMark_IsKept()
        => ChatSessionTitleGenerator.FromFirstMessage("what is eating my ram?")
               .Should().Be("What is eating my ram?");

    [Fact]
    public void FromFirstMessage_PastedMultilineText_CollapsesToOneLine()
    {
        var pasted = "Event ID 41\r\n  Kernel-Power\r\n\tunexpected shutdown";

        ChatSessionTitleGenerator.FromFirstMessage(pasted)
            .Should().Be("Event ID 41 Kernel-Power unexpected shutdown");
    }

    [Fact]
    public void FromFirstMessage_LongSentence_TruncatesOnAWordBoundary()
    {
        var input = "My computer has been running really slowly ever since the last Windows update landed";

        var title = ChatSessionTitleGenerator.FromFirstMessage(input);

        title.Length.Should().BeLessThanOrEqualTo(ChatSessionTitleGenerator.MaxLength);
        title.Should().EndWith("…");
        title.Should().StartWith("My computer has been running really slowly");
        // Broke on a space, so the character before the ellipsis is not mid-word.
        title.TrimEnd('…').Should().NotEndWith(" ");
    }

    [Fact]
    public void FromFirstMessage_LongUnbrokenToken_StillFitsTheBudget()
    {
        var title = ChatSessionTitleGenerator.FromFirstMessage(new string('x', 200));

        title.Length.Should().Be(ChatSessionTitleGenerator.MaxLength);
        title.Should().EndWith("…");
    }

    // ── Sanitize ──────────────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_EmptyRename_FallsBackToDefault()
        => ChatSessionTitleGenerator.Sanitize("   ")
               .Should().Be(ChatSessionTitleGenerator.DefaultTitle);

    [Fact]
    public void Sanitize_KeepsUserPunctuationButTrims()
        => ChatSessionTitleGenerator.Sanitize("  Fan noise.  ")
               .Should().Be("Fan noise.");

    [Fact]
    public void Sanitize_OverlongRename_IsCapped()
        => ChatSessionTitleGenerator.Sanitize(new string('a', 300))
               .Length.Should().BeLessThanOrEqualTo(ChatSessionTitleGenerator.MaxLength);

    // ── BuildPreview ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildPreview_LongBody_IsTruncatedWithEllipsis()
    {
        var preview = ChatSessionTitleGenerator.BuildPreview(new string('y', 400));

        preview.Length.Should().Be(90);
        preview.Should().EndWith("…");
    }

    [Fact]
    public void BuildPreview_CollapsesNewlines()
        => ChatSessionTitleGenerator.BuildPreview("line one\nline two")
               .Should().Be("line one line two");

    [Fact]
    public void BuildPreview_NoText_IsEmpty()
        => ChatSessionTitleGenerator.BuildPreview(null).Should().BeEmpty();
}
