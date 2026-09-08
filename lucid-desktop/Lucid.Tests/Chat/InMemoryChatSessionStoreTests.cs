using FluentAssertions;
using Lucid.Services.Chat;
using Lucid.Services.Companion;
using Xunit;

namespace Lucid.Tests.Chat;

/// <summary>
/// Behaviour of the session store the rail sits on. These tests are the contract
/// the SQLite implementation will have to satisfy when it replaces this one, so
/// they exercise the interface rather than the class's internals.
/// </summary>
public sealed class InMemoryChatSessionStoreTests
{
    private static readonly DateTimeOffset Origin =
        new(new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Local));

    private static ChatTranscriptEntry Entry(
        CompanionMessageRole     role,
        string                   text,
        DateTimeOffset?          at       = null,
        CompanionMessageCategory category = CompanionMessageCategory.Answer)
        => new()
        {
            Id        = Guid.NewGuid().ToString("N"),
            Role      = role,
            Text      = text,
            Timestamp = at ?? Origin,
            Category  = category,
        };

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithoutTitle_IsAutoTitledAndEmpty()
    {
        var store = new InMemoryChatSessionStore(() => Origin);

        var session = await store.CreateAsync();

        session.Title.Should().Be(ChatSessionTitleGenerator.DefaultTitle);
        session.IsAutoTitled.Should().BeTrue();
        session.MessageCount.Should().Be(0);
        session.CreatedAt.Should().Be(Origin);
        (await store.LoadTranscriptAsync(session.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WithTitle_IsNotAutoTitled()
    {
        var store = new InMemoryChatSessionStore(() => Origin);

        var session = await store.CreateAsync("Fan noise investigation");

        session.Title.Should().Be("Fan noise investigation");
        session.IsAutoTitled.Should().BeFalse();
    }

    // ── Append + auto-titling ─────────────────────────────────────────────────

    [Fact]
    public async Task AppendAsync_FirstUserMessage_NamesTheSession()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync();

        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.User, "why is my disk full?"));

        (await store.GetAsync(session.Id))!.Title.Should().Be("Why is my disk full?");
    }

    [Fact]
    public async Task AppendAsync_LaterUserMessages_DoNotRenameTheSession()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync();

        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.User, "first question"));
        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.System, "an answer"));
        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.User, "a completely different topic"));

        (await store.GetAsync(session.Id))!.Title.Should().Be("First question");
    }

    [Fact]
    public async Task AppendAsync_SystemMessageBeforeAnyUserMessage_DoesNotNameTheSession()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync();

        await store.AppendAsync(session.Id,
            Entry(CompanionMessageRole.System, "Ollama is not running", category: CompanionMessageCategory.Warning));

        // A setup warning is not what the conversation is about.
        (await store.GetAsync(session.Id))!.Title.Should().Be(ChatSessionTitleGenerator.DefaultTitle);
    }

    [Fact]
    public async Task AppendAsync_UpdatesCountPreviewAndTimestamp()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync();
        var later   = Origin.AddMinutes(5);

        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.User,   "hello", Origin));
        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.System, "Your disk is at 94 percent.", later));

        var updated = (await store.GetAsync(session.Id))!;
        updated.MessageCount.Should().Be(2);
        updated.UpdatedAt.Should().Be(later);
        updated.Preview.Should().Be("Your disk is at 94 percent.");
    }

    [Fact]
    public async Task AppendAsync_UnknownSession_IsIgnored()
    {
        var store = new InMemoryChatSessionStore(() => Origin);

        // A message can finalise just after the user deleted its conversation.
        var append = async () => await store.AppendAsync("does-not-exist",
            Entry(CompanionMessageRole.User, "orphan"));

        await append.Should().NotThrowAsync();
        (await store.ListAsync()).Should().BeEmpty();
    }

    // ── Transcript ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadTranscriptAsync_ReturnsMessagesInOrder()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync();

        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.User,   "one"));
        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.System, "two"));
        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.User,   "three"));

        (await store.LoadTranscriptAsync(session.Id))
            .Select(e => e.Text).Should().ContainInOrder("one", "two", "three");
    }

    [Fact]
    public async Task LoadTranscriptAsync_ReturnsASnapshot_NotALiveView()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync();
        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.User, "one"));

        var snapshot = await store.LoadTranscriptAsync(session.Id);
        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.System, "two"));

        snapshot.Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadTranscriptAsync_UnknownSession_IsEmpty()
        => (await new InMemoryChatSessionStore().LoadTranscriptAsync("nope")).Should().BeEmpty();

    // ── Rename ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenameAsync_StopsAutoTitlingPermanently()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync();

        await store.RenameAsync(session.Id, "Thermals");
        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.User, "my pc is loud"));

        var updated = (await store.GetAsync(session.Id))!;
        updated.Title.Should().Be("Thermals");
        updated.IsAutoTitled.Should().BeFalse();
    }

    [Fact]
    public async Task RenameAsync_BlankName_FallsBackToTheDefaultRatherThanAnEmptyRow()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync("Original");

        await store.RenameAsync(session.Id, "   ");

        (await store.GetAsync(session.Id))!.Title.Should().Be(ChatSessionTitleGenerator.DefaultTitle);
    }

    // ── Pin ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetPinnedAsync_MovesTheSessionToTheTopOfTheList()
    {
        var store = new InMemoryChatSessionStore(() => Origin);

        var older = await store.CreateAsync("Older");
        await store.AppendAsync(older.Id, Entry(CompanionMessageRole.User, "a", Origin));

        var newer = await store.CreateAsync("Newer");
        await store.AppendAsync(newer.Id, Entry(CompanionMessageRole.User, "b", Origin.AddHours(2)));

        (await store.ListAsync())[0].Title.Should().Be("Newer");

        await store.SetPinnedAsync(older.Id, true);

        (await store.ListAsync())[0].Title.Should().Be("Older");
    }

    [Fact]
    public async Task SetPinnedAsync_CanBeReversed()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync();

        await store.SetPinnedAsync(session.Id, true);
        await store.SetPinnedAsync(session.Id, false);

        (await store.GetAsync(session.Id))!.IsPinned.Should().BeFalse();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesTheSessionAndItsTranscript()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync();
        await store.AppendAsync(session.Id, Entry(CompanionMessageRole.User, "hello"));

        await store.DeleteAsync(session.Id);

        (await store.GetAsync(session.Id)).Should().BeNull();
        (await store.LoadTranscriptAsync(session.Id)).Should().BeEmpty();
        (await store.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_Twice_IsHarmless()
    {
        var store   = new InMemoryChatSessionStore(() => Origin);
        var session = await store.CreateAsync();

        await store.DeleteAsync(session.Id);
        var second = async () => await store.DeleteAsync(session.Id);

        await second.Should().NotThrowAsync();
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_IsEmptyForAFreshStore()
        => (await new InMemoryChatSessionStore().ListAsync()).Should().BeEmpty();

    [Fact]
    public async Task ListAsync_ReturnsSessionsInRailOrder()
    {
        var store = new InMemoryChatSessionStore(() => Origin);

        var first  = await store.CreateAsync("First");
        var second = await store.CreateAsync("Second");
        var third  = await store.CreateAsync("Third");

        await store.AppendAsync(first.Id,  Entry(CompanionMessageRole.User, "a", Origin.AddMinutes(1)));
        await store.AppendAsync(second.Id, Entry(CompanionMessageRole.User, "b", Origin.AddMinutes(3)));
        await store.AppendAsync(third.Id,  Entry(CompanionMessageRole.User, "c", Origin.AddMinutes(2)));

        (await store.ListAsync()).Select(s => s.Title)
            .Should().ContainInOrder("Second", "Third", "First");
    }
}
