using System.Text.Json;
using System.Text.Json.Serialization;
using Lucid.Core.Infrastructure;
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
///
/// Persistence integrity (this ledger is the accountability record for every
/// autonomous action, so its durability is a trust requirement):
///   • Disk writes are serialized through a single-writer gate — overlapping
///     mutations never produce interleaved or torn writes to the file.
///   • Each write goes to a temp file first and then atomically replaces the
///     ledger, so a crash mid-write can never leave truncated JSON behind.
///   • Persistence failures are surfaced to the structured logger instead of
///     being silently dropped.
/// </summary>
public sealed class AutomationAuditService : IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Number of days to retain audit entries. Entries older than this are pruned.</summary>
    public const int RetentionDays = 30;

    private static readonly string DefaultAuditFilePath = Path.Combine(
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

    /// <summary>Serializes disk writes — exactly one persist runs at a time.</summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private readonly string        _auditFilePath;
    private readonly ILucidLogger? _logger;
    private volatile bool          _disposed;

    /// <summary>
    /// The most recently scheduled persist task. Dispose waits on this (not on
    /// the gate) so a write that is scheduled but not yet started still gets
    /// its chance to land before handles are released.
    /// </summary>
    private volatile Task _lastPersist = Task.CompletedTask;

    // ── Construction / persistence ────────────────────────────────────────────

    /// <param name="logger">
    /// Optional structured logger; persistence failures are reported through it.
    /// </param>
    /// <param name="auditFilePath">
    /// Optional override of the ledger location — used by tests so they never
    /// touch the real user ledger. Defaults to %LOCALAPPDATA%\Lucid\audit-log.json.
    /// </param>
    public AutomationAuditService(ILucidLogger? logger = null, string? auditFilePath = null)
    {
        _logger        = logger;
        _auditFilePath = string.IsNullOrWhiteSpace(auditFilePath)
            ? DefaultAuditFilePath
            : auditFilePath;

        LoadFromDisk();
        PruneStale();
    }

    /// <summary>
    /// Waits briefly for any in-flight disk write to finish, then releases the
    /// lock and write-gate handles. Called at app shutdown.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Give the last scheduled persist a moment to complete so the final
        // audit entries reach disk; never block shutdown indefinitely.
        try { _lastPersist.Wait(TimeSpan.FromSeconds(3)); }
        catch { /* persist failures are already logged inside the task */ }

        _writeGate.Dispose();
        _lock.Dispose();
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
        if (IsDisposedThenWarn($"outcome update for {actionId}")) return;

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
        if (IsDisposedThenWarn($"rollback record for {actionId}")) return;

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
        if (_disposed) return [];

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
        if (_disposed) return [];

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
        if (_disposed) return [];

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
        if (_disposed) return 0;

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
        if (IsDisposedThenWarn($"audit entry for {entry.ActionId}")) return;

        _lock.EnterWriteLock();
        try { _entries.Add(entry); }
        finally { _lock.ExitWriteLock(); }

        PersistAsync();
    }

    /// <summary>
    /// Shutdown guard for mutating entry points: after Dispose the sync
    /// primitives are released, so late calls must not touch them — and the
    /// drop must be visible in the log, never silent (this ledger is the
    /// accountability record). Returns true when the call must be skipped.
    /// </summary>
    private bool IsDisposedThenWarn(string what)
    {
        if (!_disposed) return false;

        _logger?.Warning("Trust",
            $"Audit ledger received {what} after shutdown; it was not recorded.");
        return true;
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
            if (!File.Exists(_auditFilePath)) return;
            var json    = File.ReadAllText(_auditFilePath);
            var loaded  = JsonSerializer.Deserialize<List<AutomationAuditEntry>>(json, _jsonOptions);
            if (loaded is null) return;
            _lock.EnterWriteLock();
            try { _entries.AddRange(loaded); }
            finally { _lock.ExitWriteLock(); }
        }
        catch (Exception ex)
        {
            // Non-fatal — start with an empty ledger, but never silently:
            // losing the audit history is a trust-relevant event.
            _logger?.Warning("Trust",
                $"Audit ledger could not be read ({_auditFilePath}); starting with an empty ledger.", ex);
        }
    }

    private void PersistAsync()
    {
        if (_disposed) return;
        _lastPersist = Task.Run(PersistCoreAsync);
    }

    private async Task PersistCoreAsync()
    {
        try
        {
            // Single-writer gate: overlapping mutations queue here, and each
            // pass writes the then-current snapshot — the file on disk is
            // always one complete, consistent serialization.
            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var dir = Path.GetDirectoryName(_auditFilePath)!;
                Directory.CreateDirectory(dir);

                IReadOnlyList<AutomationAuditEntry> snapshot;
                _lock.EnterReadLock();
                try { snapshot = [.. _entries]; }
                finally { _lock.ExitReadLock(); }

                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);

                // Write-then-rename: a crash mid-write leaves only a stale
                // .tmp behind, never a truncated ledger.
                var tmpPath = _auditFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpPath, json).ConfigureAwait(false);
                File.Move(tmpPath, _auditFilePath, overwrite: true);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Shutdown raced an in-flight persist — the Dispose grace window
            // already gave pending writes their chance.
        }
        catch (Exception ex)
        {
            // The app keeps running, but the failure must be visible: this
            // ledger is the accountability record for autonomous actions.
            _logger?.Error("Trust",
                $"Audit ledger write failed ({_auditFilePath}); recent entries are only in memory.", ex);
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
