using FluentAssertions;
using Lucid.Services.Chat;
using Xunit;

namespace Lucid.Tests.Chat;

/// <summary>
/// Rail ordering and date bucketing. "Now" is injected throughout so the
/// midnight and DST-adjacent cases are actually reachable in a test.
/// </summary>
public sealed class ChatSessionOrganizerTests
{
    /// <summary>
    /// Anchored in local time on purpose. The organizer buckets by *calendar day*
    /// and therefore reads DateTimeOffset.LocalDateTime — timestamps built at a
    /// fixed UTC offset would land on a different local day depending on where
    /// the test runs, and the midnight case below would silently stop testing
    /// what it claims to. Midday keeps every case clear of a DST hour shift.
    /// </summary>
    private static readonly DateTimeOffset Now =
        new(new DateTime(2026, 8, 16, 14, 30, 0, DateTimeKind.Local));

    private static ChatSessionSummary Session(
        string          title,
        DateTimeOffset  updatedAt,
        bool            pinned  = false,
        string          preview = "")
        => new()
        {
            Id        = Guid.NewGuid().ToString("N"),
            Title     = title,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
            IsPinned  = pinned,
            Preview   = preview,
        };

    // ── Sort ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Sort_PinnedSessionsComeFirstEvenWhenOlder()
    {
        var pinnedOld = Session("Pinned but ancient", Now.AddDays(-90), pinned: true);
        var freshest  = Session("Fresh", Now.AddMinutes(-1));

        var sorted = ChatSessionOrganizer.Sort([freshest, pinnedOld]);

        sorted[0].Title.Should().Be("Pinned but ancient");
        sorted[1].Title.Should().Be("Fresh");
    }

    [Fact]
    public void Sort_UnpinnedSessionsAreMostRecentFirst()
    {
        var older  = Session("Older",  Now.AddHours(-5));
        var newer  = Session("Newer",  Now.AddHours(-1));
        var middle = Session("Middle", Now.AddHours(-3));

        ChatSessionOrganizer.Sort([older, newer, middle])
            .Select(s => s.Title)
            .Should().ContainInOrder("Newer", "Middle", "Older");
    }

    [Fact]
    public void Sort_IdenticalTimestamps_FallBackToTitleSoOrderIsStable()
    {
        var timestamp = Now.AddHours(-2);

        var first  = ChatSessionOrganizer.Sort([Session("Beta", timestamp), Session("Alpha", timestamp)]);
        var second = ChatSessionOrganizer.Sort([Session("Alpha", timestamp), Session("Beta", timestamp)]);

        first.Select(s => s.Title).Should().ContainInOrder("Alpha", "Beta");
        second.Select(s => s.Title).Should().ContainInOrder("Alpha", "Beta");
    }

    // ── Classify ──────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_PinnedWins_RegardlessOfAge()
        => ChatSessionOrganizer.Classify(Session("p", Now.AddDays(-400), pinned: true), Now)
               .Should().Be(ChatSessionGroupKind.Pinned);

    [Theory]
    [InlineData(0,   ChatSessionGroupKind.Today)]
    [InlineData(1,   ChatSessionGroupKind.Yesterday)]
    [InlineData(3,   ChatSessionGroupKind.PreviousWeek)]
    [InlineData(7,   ChatSessionGroupKind.PreviousWeek)]
    [InlineData(8,   ChatSessionGroupKind.PreviousMonth)]
    [InlineData(30,  ChatSessionGroupKind.PreviousMonth)]
    [InlineData(31,  ChatSessionGroupKind.Older)]
    [InlineData(365, ChatSessionGroupKind.Older)]
    public void Classify_BucketsByCalendarDayDistance(int daysAgo, ChatSessionGroupKind expected)
        => ChatSessionOrganizer.Classify(Session("s", Now.AddDays(-daysAgo)), Now)
               .Should().Be(expected);

    [Fact]
    public void Classify_LateLastNight_ReadAfterMidnight_IsYesterdayNotToday()
    {
        // 11:40pm on the 15th, being looked at at 00:20am on the 16th: only 40
        // minutes ago, but a different calendar day — and the rail groups by day.
        var justAfterMidnight = new DateTimeOffset(new DateTime(2026, 8, 16, 0, 20, 0, DateTimeKind.Local));
        var lateLastNight     = new DateTimeOffset(new DateTime(2026, 8, 15, 23, 40, 0, DateTimeKind.Local));

        ChatSessionOrganizer.Classify(Session("s", lateLastNight), justAfterMidnight)
            .Should().Be(ChatSessionGroupKind.Yesterday);
    }

    [Fact]
    public void Classify_TimestampInTheFuture_ClampsToToday()
    {
        // Clock skew and DST rollbacks can leave a session stamped slightly ahead.
        // It must still land in a real group rather than falling through.
        ChatSessionOrganizer.Classify(Session("s", Now.AddHours(6)), Now)
            .Should().Be(ChatSessionGroupKind.Today);
    }

    // ── Group ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Group_ProducesGroupsInRenderOrder_AndOmitsEmptyOnes()
    {
        var sessions = new[]
        {
            Session("Old thing",   Now.AddDays(-60)),
            Session("Pinned note", Now.AddDays(-2), pinned: true),
            Session("This morning", Now.AddHours(-4)),
        };

        var groups = ChatSessionOrganizer.Group(sessions, Now);

        groups.Select(g => g.Kind).Should().ContainInOrder(
            ChatSessionGroupKind.Pinned,
            ChatSessionGroupKind.Today,
            ChatSessionGroupKind.Older);

        groups.Should().NotContain(g => g.Sessions.Count == 0);
        groups.Should().HaveCount(3);
    }

    [Fact]
    public void Group_LabelsEveryGroup()
        => ChatSessionOrganizer.Group([Session("s", Now)], Now)
               .Single().Label.Should().Be("Today");

    [Fact]
    public void Group_NoSessions_ReturnsNoGroups()
        => ChatSessionOrganizer.Group([], Now).Should().BeEmpty();

    [Fact]
    public void Group_PinnedSessionAppearsOnlyInThePinnedGroup()
    {
        var groups = ChatSessionOrganizer.Group([Session("Today and pinned", Now, pinned: true)], Now);

        groups.Should().ContainSingle();
        groups.Single().Kind.Should().Be(ChatSessionGroupKind.Pinned);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_BlankQuery_MatchesEverything(string? query)
    {
        var sessions = new[] { Session("a", Now), Session("b", Now) };

        ChatSessionOrganizer.Search(sessions, query).Should().HaveCount(2);
    }

    [Fact]
    public void Search_MatchesTitleCaseInsensitively()
    {
        var sessions = new[] { Session("Fan Noise", Now), Session("Disk space", Now) };

        ChatSessionOrganizer.Search(sessions, "fan")
            .Should().ContainSingle().Which.Title.Should().Be("Fan Noise");
    }

    [Fact]
    public void Search_AlsoMatchesThePreviewText()
    {
        var sessions = new[]
        {
            Session("Untitled", Now, preview: "the GPU driver was reinstalled"),
            Session("Something else", Now, preview: "storage cleanup"),
        };

        ChatSessionOrganizer.Search(sessions, "gpu")
            .Should().ContainSingle().Which.Title.Should().Be("Untitled");
    }

    [Fact]
    public void Search_NoMatches_ReturnsEmpty()
        => ChatSessionOrganizer.Search([Session("Fan noise", Now)], "bluetooth")
               .Should().BeEmpty();
}
