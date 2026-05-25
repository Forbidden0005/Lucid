using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Governance;
using Lucid.Services.Remediation;
using Microsoft.UI.Xaml;

namespace Lucid.ViewModels;

// ── Sub-ViewModels ─────────────────────────────────────────────────────────────

/// <summary>ViewModel for a single suggested workflow card in the Workflow Planner.</summary>
public sealed partial class SuggestedWorkflowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _workflowId = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _rationale = "";

    [ObservableProperty]
    private string _riskLabel = "";

    [ObservableProperty]
    private string _riskColor = "#6B7280";

    [ObservableProperty]
    private string _trustRequiredLabel = "";

    [ObservableProperty]
    private int _confidencePercent;

    [ObservableProperty]
    private string _expectedOutcome = "";

    [ObservableProperty]
    private string _glyph = "";

    [ObservableProperty]
    private string _estimatedDuration = "";

    [ObservableProperty]
    private bool _isReversible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReversibleBadgeVisibility))]
    private bool _showReversibleBadge;

    public Visibility ReversibleBadgeVisibility =>
        _showReversibleBadge ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>ViewModel for a single step in the active execution progress list.</summary>
public sealed partial class WorkflowStepProgressViewModel : ObservableObject
{
    [ObservableProperty]
    private string _stepName = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _statusColor = "#6B7280";

    [ObservableProperty]
    private string _glyph = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private string _duration = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationVisibility))]
    private bool _hasDuration;

    public Visibility DurationVisibility =>
        _hasDuration ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>ViewModel for the outcome / validation results card.</summary>
public sealed partial class OutcomeReportViewModel : ObservableObject
{
    [ObservableProperty]
    private string _workflowName = "";

    [ObservableProperty]
    private string _stabilizationLabel = "";

    [ObservableProperty]
    private string _stabilizationColor = "#6B7280";

    [ObservableProperty]
    private string _stabilizationGlyph = "";

    [ObservableProperty]
    private string _narrativeSummary = "";

    [ObservableProperty]
    private string _cpuChange = "";

    [ObservableProperty]
    private string _ramChange = "";

    [ObservableProperty]
    private string _diskChange = "";

    [ObservableProperty]
    private string _insightsResolved = "";

    [ObservableProperty]
    private string _completedAt = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuChangeVisibility))]
    private bool _hasCpuChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RamChangeVisibility))]
    private bool _hasRamChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskChangeVisibility))]
    private bool _hasDiskChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InsightsResolvedVisibility))]
    private bool _hasInsightsResolved;

    public Visibility CpuChangeVisibility     => _hasCpuChange         ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RamChangeVisibility     => _hasRamChange          ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DiskChangeVisibility    => _hasDiskChange         ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InsightsResolvedVisibility => _hasInsightsResolved ? Visibility.Visible : Visibility.Collapsed;
}

// ── Main ViewModel ─────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the Autonomous Remediation page.
///
/// Exposes:
///   • Suggested workflows from the planner (sorted by confidence)
///   • Active workflow execution status and step progress
///   • Trust level controls
///   • Post-remediation validation outcome
///   • Safety pre-check warnings
/// </summary>
public sealed partial class AutonomousRemediationViewModel : ObservableObject
{
    private readonly AutonomousRemediationService _service;

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<SuggestedWorkflowViewModel>   Suggestions  { get; } = [];
    public ObservableCollection<WorkflowStepProgressViewModel> StepProgress { get; } = [];

    // ── Loading / empty state ─────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoSuggestionsVisibility))]
    private bool _hasNoSuggestions = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveWorkflowVisibility))]
    private bool _isExecuting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutcomeVisibility))]
    private bool _hasOutcome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SafetyWarningVisibility))]
    private bool _hasSafetyWarning;

    [ObservableProperty]
    private string _safetyWarningText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaltButtonVisibility))]
    private bool _canHalt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RollbackButtonVisibility))]
    private bool _canRollback;

    public Visibility LoadingVisibility       => _isLoading          ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoSuggestionsVisibility => _hasNoSuggestions   ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActiveWorkflowVisibility => _isExecuting        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OutcomeVisibility       => _hasOutcome          ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SafetyWarningVisibility => _hasSafetyWarning    ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HaltButtonVisibility    => _canHalt             ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RollbackButtonVisibility => _canRollback        ? Visibility.Visible : Visibility.Collapsed;

    // ── Active workflow status ─────────────────────────────────────────────────
    [ObservableProperty]
    private string _activeWorkflowName = "";

    [ObservableProperty]
    private string _activeWorkflowStatus = "";

    [ObservableProperty]
    private string _activeWorkflowStatusColor = "#6B7280";

    [ObservableProperty]
    private double _activeWorkflowProgress;

    [ObservableProperty]
    private string _activeWorkflowPhase = "";

    // ── Trust level controls ──────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrustLevelLabel))]
    [NotifyPropertyChangedFor(nameof(TrustLevelDescription))]
    private int _trustLevelIndex;

    public string TrustLevelLabel => _trustLevelIndex switch
    {
        0 => "Manual Only",
        1 => "Guided Approval",
        2 => "Supervised Automation",
        3 => "Recovery Automation",
        _ => "Unknown",
    };

    public string TrustLevelDescription => _trustLevelIndex switch
    {
        0 => "Workflows are suggested only. No automated execution. You review and run every step manually.",
        1 => "You approve each workflow before it runs. Steps execute automatically once approved.",
        2 => "Approved workflows run unattended. Confirmation steps are handled automatically.",
        3 => "All of the above, plus automatic rollback if stabilization worsens after a workflow.",
        _ => "",
    };

    // ── Outcome card ──────────────────────────────────────────────────────────
    [ObservableProperty]
    private OutcomeReportViewModel _outcome = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public AutonomousRemediationViewModel(AutonomousRemediationService service)
    {
        _service = service;

        // Sync trust level index from current service state
        _trustLevelIndex = (int)service.TrustLevel;

        // Subscribe to service events
        _service.SuggestionsUpdated += OnSuggestionsUpdated;
        _service.ExecutionUpdated   += OnExecutionUpdated;
        _service.ExecutionCompleted += OnExecutionCompleted;
        _service.TrustLevelChanged  += OnTrustLevelChanged;

        // Seed from current state
        RebuildSuggestions(service.Suggestions);

        if (service.LastOutcome is not null)
        {
            RebuildOutcome(service.LastOutcome);
            HasOutcome = true;
        }

        if (service.ActiveState is not null)
        {
            ApplyExecutionState(service.ActiveState);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task StartWorkflowAsync(string workflowId)
    {
        if (IsExecuting) return;

        HasSafetyWarning = false;
        SafetyWarningText = "";

        IsLoading = true;
        try
        {
            var state = await _service.StartWorkflowAsync(workflowId).ConfigureAwait(true);

            if (state is null)
            {
                // Either a duplicate execution or an unknown workflow ID
                return;
            }

            if (state.Status == WorkflowStatus.Halted && state.StepResults.Count == 0)
            {
                // Pre-condition block — show as a safety warning
                HasSafetyWarning  = true;
                SafetyWarningText = state.HaltReason ?? "Workflow blocked by safety check.";
                return;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void HaltWorkflow()
    {
        _service.HaltActiveWorkflow();
    }

    [RelayCommand]
    private async Task RollbackWorkflowAsync()
    {
        if (IsExecuting) return;
        IsLoading = true;
        try
        {
            await _service.RollbackActiveWorkflowAsync().ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SetTrustLevel(int index)
    {
        TrustLevelIndex = index;
        _service.TrustLevel = (AutomationTrustLevel)index;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnSuggestionsUpdated(object? sender, IReadOnlyList<RemediationPlanSuggestion> suggestions)
    {
        RebuildSuggestions(suggestions);
    }

    private void OnExecutionUpdated(object? sender, WorkflowExecutionState state)
    {
        ApplyExecutionState(state);
    }

    private void OnExecutionCompleted(object? sender, RemediationOutcomeReport outcome)
    {
        IsExecuting  = false;
        CanHalt      = false;
        RebuildOutcome(outcome);
        HasOutcome   = true;

        // Refresh rollback availability
        CanRollback = _service.ActiveState?.StepResults
            .Any(r => r.CanRollback) ?? false;
    }

    private void OnTrustLevelChanged(object? sender, EventArgs _)
    {
        TrustLevelIndex = (int)_service.TrustLevel;
    }

    // ── State builders ────────────────────────────────────────────────────────

    private void RebuildSuggestions(IReadOnlyList<RemediationPlanSuggestion> suggestions)
    {
        Suggestions.Clear();

        foreach (var s in suggestions)
        {
            Suggestions.Add(new SuggestedWorkflowViewModel
            {
                WorkflowId          = s.Workflow.WorkflowId,
                Name                = s.Workflow.Name,
                Description         = s.Workflow.Description,
                Rationale           = s.Rationale,
                RiskLabel           = SafetyConstraintEngine.RiskLabel(s.Workflow.Risk),
                RiskColor           = SafetyConstraintEngine.RiskColor(s.Workflow.Risk),
                TrustRequiredLabel  = SafetyConstraintEngine.TrustLevelLabel(s.Workflow.TrustLevelRequired),
                ConfidencePercent   = s.ConfidencePercent,
                ExpectedOutcome     = s.ExpectedOutcomeText,
                Glyph               = s.Workflow.Glyph,
                EstimatedDuration   = FormatDuration(s.Workflow.EstimatedDurationSeconds),
                IsReversible        = s.Workflow.IsReversible,
                ShowReversibleBadge = s.Workflow.IsReversible,
            });
        }

        HasNoSuggestions = Suggestions.Count == 0;
    }

    private void ApplyExecutionState(WorkflowExecutionState state)
    {
        var isRunning = state.Status == WorkflowStatus.Running;

        IsExecuting              = isRunning;
        CanHalt                  = isRunning;
        ActiveWorkflowName       = state.Workflow?.Name ?? "Unknown workflow";
        ActiveWorkflowStatus     = state.StatusText;
        ActiveWorkflowProgress   = state.ProgressPercent;
        ActiveWorkflowPhase      = FormatPhase(state.CurrentPhase);
        ActiveWorkflowStatusColor = StatusColor(state.Status);

        // Rebuild step progress list
        StepProgress.Clear();
        foreach (var step in state.StepResults)
        {
            var (glyph, color) = StepGlyphAndColor(step.Outcome);
            StepProgress.Add(new WorkflowStepProgressViewModel
            {
                StepName    = step.StepName,
                StatusText  = step.StatusMessage ?? OutcomeLabel(step.Outcome),
                StatusColor = color,
                Glyph       = glyph,
                IsRunning   = step.Outcome == StepOutcome.Running,
                IsComplete  = step.Outcome is StepOutcome.Succeeded or StepOutcome.PartialSuccess,
                IsFailed    = step.Outcome is StepOutcome.Failed,
                Duration    = step.StartedAt.HasValue
                              ? $"{step.Duration.TotalSeconds:F1}s"
                              : "",
                HasDuration = step.StartedAt.HasValue,
            });
        }
    }

    private void RebuildOutcome(RemediationOutcomeReport report)
    {
        var (stabilLabel, stabilColor, stabilGlyph) = StabilizationDisplay(report.StabilizationResult);

        var cpu  = report.CpuChangePercent;
        var ram  = report.RamChangePercent;
        var disk = report.DiskChangePercent;

        Outcome = new OutcomeReportViewModel
        {
            WorkflowName          = report.WorkflowName,
            StabilizationLabel    = stabilLabel,
            StabilizationColor    = stabilColor,
            StabilizationGlyph    = stabilGlyph,
            NarrativeSummary      = report.NarrativeSummary,
            CpuChange             = FormatDelta("CPU", cpu),
            RamChange             = FormatDelta("RAM", ram),
            DiskChange            = FormatDelta("Disk", disk),
            InsightsResolved      = report.InsightsResolved > 0
                                    ? $"{report.InsightsResolved} finding{(report.InsightsResolved == 1 ? "" : "s")} resolved"
                                    : "",
            CompletedAt           = report.CompletedAt.ToString("HH:mm:ss"),
            HasCpuChange          = Math.Abs(cpu)  > 0.5,
            HasRamChange          = Math.Abs(ram)  > 0.5,
            HasDiskChange         = Math.Abs(disk) > 0.5,
            HasInsightsResolved   = report.InsightsResolved > 0,
        };
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Cleanup()
    {
        _service.SuggestionsUpdated -= OnSuggestionsUpdated;
        _service.ExecutionUpdated   -= OnExecutionUpdated;
        _service.ExecutionCompleted -= OnExecutionCompleted;
        _service.TrustLevelChanged  -= OnTrustLevelChanged;
    }

    // ── Display helpers ───────────────────────────────────────────────────────

    private static string FormatDuration(int seconds) =>
        seconds switch
        {
            < 60  => $"~{seconds}s",
            < 120 => "~1 min",
            _     => $"~{seconds / 60} min",
        };

    private static string FormatPhase(WorkflowPhase phase) => phase switch
    {
        WorkflowPhase.PreCheck      => "Pre-check",
        WorkflowPhase.Backup        => "Backup",
        WorkflowPhase.Execution     => "Executing",
        WorkflowPhase.Stabilization => "Stabilizing",
        WorkflowPhase.Validation    => "Validating",
        WorkflowPhase.Complete      => "Complete",
        _                           => phase.ToString(),
    };

    private static string StatusColor(WorkflowStatus status) => status switch
    {
        WorkflowStatus.Running           => "#60A5FA",
        WorkflowStatus.Completed         => "#34D399",
        WorkflowStatus.RolledBack        => "#F59E0B",
        WorkflowStatus.Halted            => "#EF4444",
        WorkflowStatus.Failed            => "#EF4444",
        WorkflowStatus.Cancelled         => "#6B7280",
        _                                => "#6B7280",
    };

    private static (string glyph, string color) StepGlyphAndColor(StepOutcome outcome) => outcome switch
    {
        StepOutcome.Pending        => ("", "#6B7280"),
        StepOutcome.Running        => ("", "#60A5FA"),
        StepOutcome.Succeeded      => ("", "#34D399"),
        StepOutcome.PartialSuccess => ("", "#F59E0B"),
        StepOutcome.Failed         => ("", "#EF4444"),
        StepOutcome.Skipped        => ("", "#6B7280"),
        StepOutcome.RolledBack     => ("", "#F59E0B"),
        _                          => ("", "#6B7280"),
    };

    private static string OutcomeLabel(StepOutcome outcome) => outcome switch
    {
        StepOutcome.Pending        => "Pending",
        StepOutcome.Running        => "Running…",
        StepOutcome.Succeeded      => "Completed",
        StepOutcome.PartialSuccess => "Partial success",
        StepOutcome.Failed         => "Failed",
        StepOutcome.Skipped        => "Skipped",
        StepOutcome.RolledBack     => "Rolled back",
        _                          => outcome.ToString(),
    };

    private static (string label, string color, string glyph) StabilizationDisplay(StabilizationResult result) =>
        result switch
        {
            StabilizationResult.Improved    => ("Improved",    "#34D399", ""),
            StabilizationResult.Stable      => ("Stable",      "#60A5FA", ""),
            StabilizationResult.Degraded    => ("Metrics increased", "#EF4444", ""),
            StabilizationResult.Inconclusive => ("Inconclusive", "#6B7280", ""),
            _                               => ("Unknown",     "#6B7280", ""),
        };

    private static string FormatDelta(string label, double delta)
    {
        if (Math.Abs(delta) < 0.5) return "";
        var sign = delta > 0 ? "+" : "";
        return $"{label}: {sign}{delta:F1}%";
    }
}
