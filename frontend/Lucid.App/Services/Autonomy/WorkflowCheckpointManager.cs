using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lucid.Services.Autonomy;

/// <summary>
/// Manages workflow checkpoints, enabling pause, resume, and partial-rollback
/// for long-running autonomous workflows.
///
/// Checkpoints are saved to %LOCALAPPDATA%\Lucid\workflow-checkpoints.json.
/// On startup, any stale checkpoints (older than 24 hours) are pruned automatically.
///
/// Threading: all mutating methods are thread-safe via a lock.
/// </summary>
public sealed class WorkflowCheckpointManager
{
    private static readonly string CheckpointFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucid", "workflow-checkpoints.json");

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, WorkflowCheckpoint> _checkpoints = [];
    private readonly object _lock = new();

    public WorkflowCheckpointManager()
    {
        LoadFromDisk();
        PruneStale();
    }

    // ── Save / restore ────────────────────────────────────────────────────────

    /// <summary>
    /// Saves a checkpoint for the given workflow at its current step.
    /// Overwrites any previous checkpoint for the same workflow ID.
    /// </summary>
    public void SaveCheckpoint(AutonomousWorkflow workflow, string? narrationSummary = null)
    {
        var cp = new WorkflowCheckpoint
        {
            WorkflowId          = workflow.Id,
            StepIndex           = workflow.CurrentStepIndex,
            StatusAtCheckpoint  = workflow.Status,
            CheckpointedAt      = DateTimeOffset.Now,
            CompletedStepIds    = [.. workflow.CompletedStepIds],
            NarrationSummary    = narrationSummary,
        };

        lock (_lock)
        {
            _checkpoints[workflow.Id] = cp;
        }

        PersistAsync();
    }

    /// <summary>
    /// Returns the saved checkpoint for the given workflow ID, or null if none exists.
    /// </summary>
    public WorkflowCheckpoint? GetCheckpoint(string workflowId)
    {
        lock (_lock)
        {
            return _checkpoints.TryGetValue(workflowId, out var cp) ? cp : null;
        }
    }

    /// <summary>
    /// Removes the checkpoint for the given workflow (called when it completes or is cancelled).
    /// </summary>
    public void ClearCheckpoint(string workflowId)
    {
        lock (_lock)
        {
            _checkpoints.Remove(workflowId);
        }

        PersistAsync();
    }

    /// <summary>
    /// Returns all currently saved checkpoints (for display in Settings or Diagnostics).
    /// </summary>
    public IReadOnlyList<WorkflowCheckpoint> GetAllCheckpoints()
    {
        lock (_lock)
        {
            return [.. _checkpoints.Values];
        }
    }

    /// <summary>
    /// Restores the workflow's state from a checkpoint.
    /// The workflow's CurrentStepIndex and CompletedStepIds are updated in-place.
    /// </summary>
    public void RestoreFromCheckpoint(AutonomousWorkflow workflow, WorkflowCheckpoint checkpoint)
    {
        workflow.CurrentStepIndex = checkpoint.StepIndex;
        workflow.Status           = WorkflowLifecycleStatus.Paused;
        workflow.CompletedStepIds.Clear();
        workflow.CompletedStepIds.AddRange(checkpoint.CompletedStepIds);
    }

    // ── Disk persistence ──────────────────────────────────────────────────────

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(CheckpointFilePath)) return;
            var json   = File.ReadAllText(CheckpointFilePath);
            var loaded = JsonSerializer.Deserialize<List<WorkflowCheckpoint>>(json, _json);
            if (loaded is null) return;
            lock (_lock)
            {
                foreach (var cp in loaded)
                    _checkpoints[cp.WorkflowId] = cp;
            }
        }
        catch { /* Non-fatal */ }
    }

    private void PruneStale()
    {
        var cutoff = DateTimeOffset.Now.AddHours(-24);
        lock (_lock)
        {
            foreach (var key in _checkpoints.Keys
                .Where(k => _checkpoints[k].CheckpointedAt < cutoff)
                .ToList())
            {
                _checkpoints.Remove(key);
            }
        }
    }

    private void PersistAsync()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                List<WorkflowCheckpoint> snapshot;
                lock (_lock) { snapshot = [.. _checkpoints.Values]; }

                var dir = Path.GetDirectoryName(CheckpointFilePath)!;
                Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(snapshot, _json);
                await File.WriteAllTextAsync(CheckpointFilePath, json).ConfigureAwait(false);
            }
            catch { /* Non-fatal */ }
        });
    }
}
