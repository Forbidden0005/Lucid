using Lucid.Services.History;
using Lucid.Services.Interaction.Language;

namespace Lucid.Services.Interaction.Narrative;

/// <summary>
/// Builds plain-English narratives for completed workflow and operation outcomes.
///
/// Outcome narration answers:
///   "What happened when this action was executed? Was it successful?"
///
/// The goal is to make every execution fully understandable — not just a
/// status flag, but a meaningful sentence the user can act on.
///
/// Thread-safe: stateless and pure.
/// </summary>
public static class WorkflowOutcomeNarrator
{
    /// <summary>
    /// Builds a compact outcome summary for display in the History/Timeline card.
    /// </summary>
    public static string BuildCompact(OperationRecord record)
    {
        var label  = record.ActionTitle.Length > 0 ? record.ActionTitle : record.ActionId;
        var result = record.IsRollback  ? $"'{label}' was rolled back — changes reversed."
                   : record.WasRolledBack ? $"'{label}' was undone after execution."
                   : record.IsDryRun   ? $"'{label}' previewed only — no changes made."
                   : record.IsSuccess  ? $"'{label}' completed successfully."
                   : $"'{label}' did not complete — {(record.Message.Length > 0 ? record.Message : "no details recorded")}.";

        return CalmCommunicationFormatter.FormatCompact(result);
    }

    /// <summary>
    /// Builds a full outcome narrative for the History detail panel.
    /// </summary>
    public static string BuildFull(OperationRecord record)
    {
        var parts = new List<string>(5);

        parts.Add(BuildCompact(record));
        parts.Add($"Executed at {record.ExecutedAt:HH:mm:ss} UTC.");

        if (!string.IsNullOrWhiteSpace(record.Message))
            parts.Add($"Details: {record.Message}");

        if (record.Errors.Count > 0)
            parts.Add($"Errors: {string.Join("; ", record.Errors.Take(3))}");

        if (record.Warnings.Count > 0)
            parts.Add($"Warnings: {string.Join("; ", record.Warnings.Take(3))}");

        if (record.CanRollback && record.IsSuccess && !record.WasRolledBack)
            parts.Add("This action is reversible — use the History page to roll back if needed.");

        return CalmCommunicationFormatter.FormatDetail(string.Join(" ", parts));
    }

    /// <summary>
    /// Builds a brief session-level operations summary for the Timeline header.
    /// </summary>
    public static string BuildSessionSummary(IReadOnlyList<OperationRecord> records)
    {
        if (records.Count == 0) return "No operations recorded this session.";

        var succeeded  = records.Count(r => r.IsSuccess && !r.IsRollback);
        var failed     = records.Count(r => !r.IsSuccess && !r.IsDryRun);
        var rolledBack = records.Count(r => r.IsRollback || r.WasRolledBack);
        var previewed  = records.Count(r => r.IsDryRun);

        var parts = new List<string>(4);
        if (succeeded  > 0) parts.Add($"{succeeded} succeeded");
        if (failed     > 0) parts.Add($"{failed} failed");
        if (rolledBack > 0) parts.Add($"{rolledBack} rolled back");
        if (previewed  > 0) parts.Add($"{previewed} previewed");

        var summary = $"{records.Count} operation{(records.Count == 1 ? "" : "s")} this session — " +
                      string.Join(", ", parts) + ".";

        return CalmCommunicationFormatter.FormatStandard(summary);
    }
}
