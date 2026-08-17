using Lucid.Core.Infrastructure;
using Lucid.Services.Conversation;
using Lucid.Services.Reliability;

namespace Lucid.Services.Chat;

/// <summary>
/// What an investigation produced, ready to be handed to the model and shown to
/// the user.
/// </summary>
public sealed record InvestigationOutcome
{
    /// <summary>Nothing needed investigating.</summary>
    public static readonly InvestigationOutcome None = new();

    /// <summary>Extra system-prompt text, or null when nothing was investigated.</summary>
    public string? PromptContext { get; init; }

    /// <summary>One line for the chat, telling the user what was looked at.</summary>
    public string? TrailMessage { get; init; }

    public bool DidInvestigate => PromptContext is not null;
}

/// <summary>
/// Runs real investigations before the model answers.
/// </summary>
public interface IInvestigationPreflight
{
    /// <summary>
    /// Decides what the question requires, gathers it, and returns the result.
    /// Never throws — a failed investigation degrades to
    /// <see cref="InvestigationOutcome.None"/> so the conversation continues.
    /// </summary>
    Task<InvestigationOutcome> RunAsync(string userMessage, CancellationToken ct = default);
}

/// <summary>
/// The deterministic half of the investigation loop.
///
/// The question is classified by the existing keyword resolver, and the
/// investigations that question needs are run *before* the model is called, with
/// the results injected into its context. So the model never decides what to
/// look at — but it also never has to, which is the point: this works identically
/// on a 3b model, and works with no model at all if the deterministic responder
/// is answering instead.
///
/// The other half — the model choosing its own next step and iterating, the way a
/// person would follow a thread — needs reliable tool-calling and therefore a
/// larger model. It layers on top of this rather than replacing it: when a
/// capable model is configured, the pre-flight results become its starting
/// evidence instead of its only evidence.
///
/// Why this ordering matters: a question like "why does my PC keep crashing" is
/// unanswerable from live telemetry, because telemetry describes a machine that
/// is by definition running. Without a pre-flight the model was left inferring
/// from current CPU and RAM, which is how it ended up blaming whichever process
/// happened to be busy.
/// </summary>
public sealed class InvestigationPreflight : IInvestigationPreflight
{
    private readonly ConversationIntentResolver _resolver;
    private readonly IReliabilityService        _reliability;
    private readonly ILucidLogger?              _logger;

    public InvestigationPreflight(
        ConversationIntentResolver resolver,
        IReliabilityService        reliability,
        ILucidLogger?              logger = null)
    {
        _resolver    = resolver;
        _reliability = reliability;
        _logger      = logger;
    }

    public async Task<InvestigationOutcome> RunAsync(
        string            userMessage,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return InvestigationOutcome.None;

        var intent = _resolver.Resolve(userMessage).Intent;

        if (!NeedsCrashHistory(intent)) return InvestigationOutcome.None;

        try
        {
            var report = await _reliability.InvestigateAsync(ct: ct).ConfigureAwait(false);

            return new InvestigationOutcome
            {
                PromptContext = ReliabilityPromptWriter.Write(report),
                TrailMessage  = ReliabilityPromptWriter.DescribeInvestigation(report),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The conversation matters more than the investigation. Answering from
            // live data alone is worse than answering from the event log, but it is
            // far better than failing the user's message outright.
            _logger?.Warning("Chat", $"Crash-history pre-flight failed: {ex.Message}", ex);
            return InvestigationOutcome.None;
        }
    }

    /// <summary>
    /// Which questions warrant reading the event logs.
    ///
    /// Deliberately narrow. The read is bounded but not free, and pulling crash
    /// history into a question about disk space would spend time and context on
    /// something irrelevant. "What changed?" is included because an unexplained
    /// change and an unexplained failure are frequently the same event seen from
    /// two sides.
    /// </summary>
    internal static bool NeedsCrashHistory(ConversationIntent intent) => intent
        is ConversationIntent.WhyDoesItCrash
        or ConversationIntent.InvestigateProblem
        or ConversationIntent.WhyDidSomethingChange;
}
