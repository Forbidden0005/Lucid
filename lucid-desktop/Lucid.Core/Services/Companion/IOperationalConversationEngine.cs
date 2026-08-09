namespace Lucid.Services.Companion;

/// <summary>
/// Resolves natural-language operational queries into factual, evidence-grounded
/// companion responses.
///
/// ARCHITECTURE NOTE:
///   This is NOT a language model or AI assistant.
///   Intent resolution is deterministic keyword matching.
///   All responses are composed from live telemetry, intelligence findings,
///   timeline events, and evidence graph data.
///   No text is fabricated beyond what the platform services report.
///   Uncertainty is always communicated explicitly — no false confidence.
///
/// Supported query intents (resolved via keyword matching):
///   Performance   — "slow", "lag", "freeze", "performance"
///   Memory        — "ram", "memory"
///   Disk          — "disk", "storage", "space"
///   Cpu           — "cpu", "processor"
///   Startup       — "startup", "boot"
///   Security      — "security", "risk", "suspicious"
///   Timeline      — "changed", "recent", "today", "what happened"
///   Investigation — "investigate", "root cause", "why is this"
///   Help          — "help", "what can you do"
///   Status        — default when no specific intent matches
///
/// All responses follow the Lucid communication standard:
///   - Confidence-aware, probabilistic language
///   - Contextual explanation of WHY not just WHAT
///   - No fear-based or absolute-certainty language
///   - Every claim traceable to a specific service observation
/// </summary>
public interface IOperationalConversationEngine
{
    /// <summary>
    /// Processes a natural-language query and returns a companion message
    /// grounded in real-time service data.
    ///
    /// Never throws — returns a CompanionMessageCategory.Error message on failure.
    /// </summary>
    Task<CompanionMessage> ProcessQueryAsync(string query, CancellationToken ct = default);
}
