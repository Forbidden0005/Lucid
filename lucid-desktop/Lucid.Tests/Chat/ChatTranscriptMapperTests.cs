using FluentAssertions;
using Lucid.Services.Chat;
using Lucid.Services.Companion;
using Lucid.Services.LlmChat;
using Xunit;

namespace Lucid.Tests.Chat;

/// <summary>
/// Mapping between the rendered conversation, the stored transcript, and the
/// history handed back to the model when a session is resumed.
///
/// The last of those is the one that matters most: feeding app-generated text
/// back as assistant turns teaches the model to imitate it, so the filtering
/// rules are pinned down here.
/// </summary>
public sealed class ChatTranscriptMapperTests
{
    private static readonly DateTimeOffset At =
        new(new DateTime(2026, 8, 16, 10, 15, 0, DateTimeKind.Local));

    private static ChatTranscriptEntry Entry(
        CompanionMessageRole     role,
        string                   text,
        CompanionMessageCategory category = CompanionMessageCategory.Answer)
        => new()
        {
            Id        = "abc12345",
            Role      = role,
            Text      = text,
            Timestamp = At,
            Category  = category,
        };

    // ── Message ⇄ entry ───────────────────────────────────────────────────────

    [Fact]
    public void ToEntry_CarriesTheDurableFields()
    {
        var message = new CompanionMessage
        {
            Id        = "id-1",
            Role      = CompanionMessageRole.User,
            Text      = "why is my fan loud",
            Timestamp = At,
            Category  = CompanionMessageCategory.Answer,
        };

        var entry = ChatTranscriptMapper.ToEntry(message);

        entry.Id.Should().Be("id-1");
        entry.Role.Should().Be(CompanionMessageRole.User);
        entry.Text.Should().Be("why is my fan loud");
        entry.Timestamp.Should().Be(At);
        entry.Category.Should().Be(CompanionMessageCategory.Answer);
    }

    [Fact]
    public void ToEntry_DropsLiveEnrichment()
    {
        var message = new CompanionMessage
        {
            Id            = "id-2",
            Role          = CompanionMessageRole.System,
            Text          = "Storage is filling up.",
            Timestamp     = At,
            EvidenceItems = [new ConversationEvidenceItem { Source = "Storage", Summary = "94% used" }],
            SuggestedActions =
            [
                new SuggestedAction
                {
                    Id     = "open-storage",
                    Label  = "Open Storage",
                    Glyph  = "",
                    Target = NavigationTarget.Storage,
                },
            ],
            ConfidenceDisplay = "High 87%",
        };

        var restored = ChatTranscriptMapper.ToMessage(ChatTranscriptMapper.ToEntry(message));

        // Chips and badges describe the machine as it was at the time of the
        // answer; replaying them later would assert things that may no longer hold.
        restored.SuggestedActions.Should().BeEmpty();
        restored.EvidenceItems.Should().BeEmpty();
        restored.ConfidenceDisplay.Should().BeNull();

        restored.Text.Should().Be("Storage is filling up.");
        restored.Role.Should().Be(CompanionMessageRole.System);
    }

    [Fact]
    public void ToMessages_PreservesOrder()
    {
        var entries = new[]
        {
            Entry(CompanionMessageRole.User,   "one"),
            Entry(CompanionMessageRole.System, "two"),
            Entry(CompanionMessageRole.User,   "three"),
        };

        ChatTranscriptMapper.ToMessages(entries)
            .Select(m => m.Text).Should().ContainInOrder("one", "two", "three");
    }

    // ── Model history ─────────────────────────────────────────────────────────

    [Fact]
    public void ToLlmTurns_MapsUserAndAnswerTurns()
    {
        var entries = new[]
        {
            Entry(CompanionMessageRole.User,   "why is my disk full?"),
            Entry(CompanionMessageRole.System, "Your system drive is at 94 percent."),
        };

        var turns = ChatTranscriptMapper.ToLlmTurns(entries);

        turns.Should().HaveCount(2);
        turns[0].Role.Should().Be(LlmTurnRole.User);
        turns[0].Text.Should().Be("why is my disk full?");
        turns[1].Role.Should().Be(LlmTurnRole.Assistant);
        turns[1].Text.Should().Be("Your system drive is at 94 percent.");
    }

    [Fact]
    public void ToLlmTurns_KeepsInsightsWhichAreAlsoModelAuthored()
    {
        var turns = ChatTranscriptMapper.ToLlmTurns(
        [
            Entry(CompanionMessageRole.System, "Startup is busier than usual.", CompanionMessageCategory.Insight),
        ]);

        turns.Should().ContainSingle().Which.Role.Should().Be(LlmTurnRole.Assistant);
    }

    [Theory]
    [InlineData(CompanionMessageCategory.Error)]
    [InlineData(CompanionMessageCategory.Warning)]
    [InlineData(CompanionMessageCategory.Welcome)]
    [InlineData(CompanionMessageCategory.Action)]
    [InlineData(CompanionMessageCategory.Workflow)]
    public void ToLlmTurns_ExcludesAppGeneratedText(CompanionMessageCategory category)
    {
        var turns = ChatTranscriptMapper.ToLlmTurns(
        [
            Entry(CompanionMessageRole.System, "Ollama is not running.", category),
        ]);

        turns.Should().BeEmpty();
    }

    [Fact]
    public void ToLlmTurns_UserTurnsSurviveEvenBesideExcludedSystemText()
    {
        var turns = ChatTranscriptMapper.ToLlmTurns(
        [
            Entry(CompanionMessageRole.User,   "is my pc ok?"),
            Entry(CompanionMessageRole.System, "Could not reach Ollama.", CompanionMessageCategory.Error),
            Entry(CompanionMessageRole.User,   "try again"),
        ]);

        turns.Should().HaveCount(2);
        turns.Should().OnlyContain(t => t.Role == LlmTurnRole.User);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToLlmTurns_DropsEmptyMessages(string text)
    {
        // A response cancelled before the first token leaves an empty bubble.
        var turns = ChatTranscriptMapper.ToLlmTurns([Entry(CompanionMessageRole.System, text)]);

        turns.Should().BeEmpty();
    }

    [Fact]
    public void ToLlmTurns_EmptyTranscript_ProducesNoTurns()
        => ChatTranscriptMapper.ToLlmTurns([]).Should().BeEmpty();
}
