using System.Text.Json;
using System.Text.Json.Serialization;
using Lucid.Services.Automation;

namespace Lucid.Services.Trust;

/// <summary>
/// Append-oriented audit ledger for all automation consent decisions and action outcomes.
///
/// Every action that passes through the consent gate — whether auto-approved, user-approved,
/// or denied — produces an <see cref="AutomationAuditEntry"/> written to this ledger.
///
/// The ledger is:
///   • In-memory during the session (all queries are O(n) on the current collection)
///   • Persisted to JSON on disk at <c>%LOCALAPPDATA%\Lucid\audit-log.json</c>
///   • Trimmed to the last <see cref="RetentionDays"/> days on startup and every hour
///
/// Threading:
///   All public mutating methods are thread-safe via a lock.
///   All reads return snapshots — safe to consume from any thread.
/// </summary>
public sealed class AutomationAuditService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Number of days to retain audit entries. Entries older than this are pruned.</summary>
    public const int RetentionDays = 30;

    private static readonly string AuditFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucid", "audit-log.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() },
    };

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly List<AutomationAuditEntry>       _entries = [];
    private readonly System.Threading.ReaderWriterLockSlim _lock = new();

    // ── Construction / persistence ────────────────────────────────────────────

    public AutomationAuditService()
    {
        LoadFromDisk();
        PruneStale();
    }

    // ── Recording (called by AutomationConsentService) ────────────────────────

    /// <summary>
    /// Records a step that was blocked by the boundary policy before reaching the user.
    /// </summary>
    public void RecordBlockedAction(AutomationStep step, string workflowId, string reason)
    {
        AddEntry(new AutomationAuditEntry
        {
            Id              = AutomationAuditEntry.NewId(),
            RequestedAt     = DateTimeOffset.Now,
            WorkflowId      = workflowId,
            ActionTitle     = step.Title,
            ActionId        = step.ActionId,
            Scope           = PermissionScope.SystemInfoRead,  // safest fallback
            Risk            = TrustRiskLevel.Critical,
            EvidenceSummary = $"Action blocked by boundary policy: {reason}",
            Approved        = false,
            DecidedAt       = DateTimeOffset.Now,
            Succeeded       = false,
            ErrorDetail     = reason,
        });
    }

    /// <summary>
    /// Records a step that was auto-approved (not shown to the user) because
    /// the current consent mode permits it.
    /// </summary>
    public void RecordAutoApproved(AutomationStep step, string workflowId, PermissionScope scope)
    {
        AddEntry(new AutomationAuditEntry
        {
            Id              = AutomationAuditEntry.NewId(),
            RequestedAt     = DateTimeOffset.Now,
            WorkflowId      = workflowId,
            ActionTitle     = step.Title,
            ActionId        = step.ActionId,
            Scope           = scope,
            Risk            = MapRisk(step.Risk),
            EvidenceSummary = step.Rationale ?? string.Empty,
            Approved        = true,
            DecidedAt       = DateTimeOffset.Now,
        });
    }

    /// <summary>
    /// Records a step that the user explicitly approved via the consent card.
    /// </summary>
    public void RecordApproved(
        AutomationStep  step,
        string          workflowId,
        PermissionScope scope,
        ConsentRequest  request,
        ConsentResponse response)
    {
        AddEntry(new AutomationAuditEntry
        {
            Id              = AutomationAuditEntry.NewId(),
            RequestedAt     = request.RequestedAt,
            WorkflowId      = workflowId,
            ActionTitle     = step.Title,
            ActionId        = step.ActionId,
            Scope           = scope,
            Risk            = request.Risk,
            EvidenceSummary = request.WhySuggested,
            Approved        = true,
            DecidedAt       = response.RespondedAt,
        });
    }

    /// <summary>
    /// Records a step that the user denied via the consent card.
    /// </summary>
    public void RecordDenied(
        AutomationStep   step,
        string           workflowId,
        PermissionScope  scope,
        ConsentRequest?  request  = null,
        ConsentResponse? response = null,
        string?          reason   = null)
    {
        AddEntry(new AutomationAuditEntry
        {
            Id              = AutomationAuditEntry.NewId(),
            RequestedAt     = request?.RequestedAt ?? DateTimeOffset.Now,
            WorkflowId      = workflowId,
            ActionTitle     = step.Title,
            ActionId        = step.ActionId,
            Scope           = scope,
            Risk            = request?.Risk ?? MapRisk(step.Risk),
            EvidenceSummary = request?.WhySuggested ?? reason ?? string.Empty,
            Approved        = false,
            DecidedAt       = response?.RespondedAt ?? DateTimeOffset.Now,
            ErrorDetail     = reason,
        });
    }

    /// <summary>
    /// Updates an existing entry with the execution outcome after the step runs.
    /// Called by the orchestrator after step execution completes.
    /// </summary>
    public void UpdateOutcome(string workflowId, string actionId, bool success, string? error = null)
    {
        _lock.EnterWriteLock();
        try
        {
            // Find the most recent approved entry for this workflow+action
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.WorkflowId == workflowId &&
                    e.ActionId   == actionId   &&
                    e.Approved   == true       &&
                    e.Succeeded  is null)
                {
                    _entries[i] = e with { Succeeded = success, ErrorDetail = error };
                    break;
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        PersistAsync();
    }

    /// <summary>
    /// Records that an approved, completed action was subsequently rolled back.
    /// </summary>
    public void RecordRollback(string workflowId, string actionId)
    {
        _lock.EnterWriteLock();
        try
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.WorkflowId == workflowId && e.ActionId == actionId)
                {
                    _entries[i] = e with { WasRolledBack = true, RolledBackAt = DateTimeOffset.Now };
                    break;
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        PersistAsync();
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>Returns a snapshot of all current audit entries (newest first).</summary>
    public IReadOnlyList<AutomationAuditEntry> GetAllEntries()
    {
        _lock.EnterReadLock();
        try
        {
            return [.. _entries.OrderByDescending(e => e.RequestedAt)];
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Returns entries for a specific workflow ID.</summary>
    public IReadOnlyList<AutomationAuditEntry> GetEntriesForWorkflow(string workflowId)
    {
        _lock.EnterReadLock();
        try
        {
            return [.. _entries.Where(e => e.WorkflowId == workflowId)
                               .OrderBy(e => e.RequestedAt)];
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Returns entries within the last N days.</summary>
    public IReadOnlyList<AutomationAuditEntry> GetRecentEntries(int days = 7)
    {
        var cutoff = DateTimeOffset.Now.AddDays(-days);
        _lock.EnterReadLock();
        try
        {
            return [.. _entries.Where(e => e.RequestedAt >= cutoff)
                               .OrderByDescending(e => e.RequestedAt)];
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Returns the count of denied high-risk requests in the last 24 hours.
    /// Used by <see cref="OperationalTrustManager"/> to detect escalating caution.
    /// </summary>
    public int CountRecentHighRiskDenials(TimeSpan window)
    {
        var cutoff = DateTimeOffset.Now - window;
        _lock.EnterReadLock();
        try
        {
            return _entries.Count(e =>
                !e.Approved &&
                (e.Risk >= TrustRiskLevel.High) &&
                e.RequestedAt >= cutoff);
        }
        finally { _lock.ExitReadLock(); }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void AddEntry(AutomationAuditEntry entry)
    {
        _lock.EnterWriteLock();
        try { _entries.Add(entry); }
        finally { _lock.ExitWriteLock(); }

        PersistAsync();
    }

    private void PruneStale()
    {
        var cutoff = DateTimeOffset.Now.AddDays(-RetentionDays);
        _lock.EnterWriteLock();
        try { _entries.RemoveAll(e => e.RequestedAt < cutoff); }
        finally { _lock.ExitWriteLock(); }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(AuditFilePath)) return;
            var json    = File.ReadAllText(AuditFilePath);
            var loaded  = JsonSerializer.Deserialize<List<AutomationAuditEntry>>(json, _jsonOptions);
            if (loaded is null) return;
            _lock.EnterWriteLock();
            try { _entries.AddRange(loaded); }
            finally { _lock.ExitWriteLock(); }
        }
        catch
        {
            // Non-fatal — start with empty ledger if file is corrupted
        }
    }

    private void PersistAsync()
    {
        _ = Task.Run(PersistCoreAsync);
    }

    private async Task PersistCoreAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(AuditFilePath)!;
            Directory.CreateDirectory(dir);

            IReadOnlyList<AutomationAuditEntry> snapshot;
            _lock.EnterReadLock();
            try { snapshot = [.. _entries]; }
            finally { _lock.ExitReadLock(); }

            var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
            await File.WriteAllTextAsync(AuditFilePath, json).ConfigureAwait(false);
        }
        catch
        {
            // Non-fatal — audit persistence failure does not affect the app
        }
    }

    private static TrustRiskLevel MapRisk(AutomationRiskLevel r) => r switch
    {
        AutomationRiskLevel.None   => TrustRiskLevel.None,
        AutomationRiskLevel.Low    => TrustRiskLevel.Low,
        AutomationRiskLevel.Medium => TrustRiskLevel.Medium,
        AutomationRiskLevel.High   => TrustRiskLevel.High,
        _                          => TrustRiskLevel.High,
    };
}
