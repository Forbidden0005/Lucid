using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Services.Reasoning;
using Lucid.Services.Workflow;

namespace Lucid.ViewModels;

/// <summary>
/// ViewModel for the Unified Investigation workspace (InvestigationPage).
///
/// The investigation workspace presents 7 cohesive sections:
///   1. Evidence Graph summary — node/edge counts, graph health
///   2. Root Cause Candidates — ranked, confidence-labelled list
///   3. Primary Root Cause detail — full explanation with evidence items
///   4. Suggested Workflows — 1–3 guided remediation workflows
///   5. Workflow detail — expanded view of a selected workflow's steps
///   6. Evidence chain viewer — the connected evidence for selected root cause
///   7. Stabilization status — whether conditions improved after past actions
///
/// Architecture:
///   • Fully MVVM — no business logic in the View.
///   • BuildInvestigationAsync() is the single entry point for analysis.
///   • All data is loaded on demand when the page is navigated to.
///   • Live updates: re-builds the graph when intelligence findings change.
///
/// Threading:
///   All [ObservableProperty] writes are dispatched to the UI thread.
///   Analysis work runs on the thread pool via BuildGraphAsync().
/// </summary>
public sealed partial class InvestigationViewModel : ObservableObject
{
    // ── Services ──────────────────────────────────────────────────────────────

    private readonly IOperationalEvidenceGraph   _evidenceGraph;
    private readonly IRootCauseAnalysisEngine    _rootCauseEngine;
    private readonly IEvidenceExplanationService _explanationService;
    private readonly IOperationalWorkflowEngine  _workflowEngine;

    // ── Loading state ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _loadingMessage = "Building evidence graph…";
    [ObservableProperty] private bool   _hasError;
    [ObservableProperty] private string _errorMessage   = string.Empty;

    // ── Evidence graph summary ─────────────────────────────────────────────────

    [ObservableProperty] private int    _evidenceNodeCount;
    [ObservableProperty] private int    _evidenceLinkCount;
    [ObservableProperty] private string _graphBuiltAtLabel  = string.Empty;
    [ObservableProperty] private bool   _hasEvidenceGraph;

    // ── Root cause candidates ──────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<RootCauseCandidateViewModel> _rootCauses = [];
    [ObservableProperty] private bool                                               _hasRootCauses;
    [ObservableProperty] private string                                             _rootCauseCountLabel = string.Empty;
    [ObservableProperty] private string                                             _noCausesMessage     = "No high-confidence root causes identified. " +
                                                                                                           "The evidence graph does not yet contain sufficient data to isolate a primary cause.";

    // ── Primary root cause detail ──────────────────────────────────────────────

    [ObservableProperty] private RootCauseCandidateViewModel? _primaryRootCause;
    [ObservableProperty] private bool                          _hasPrimaryRootCause;
    [ObservableProperty] private string                        _primaryHeadline    = string.Empty;
    [ObservableProperty] private string                        _primaryWhyMatters  = string.Empty;
    [ObservableProperty] private string                        _primaryOutcome     = string.Empty;
    [ObservableProperty] private string                        _primaryNextStep    = string.Empty;
    [ObservableProperty] private string                        _primaryConfidence  = string.Empty;
    [ObservableProperty] private ObservableCollection<string>  _primaryEvidence    = [];

    // ── Suggested workflows ────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<WorkflowViewModel> _suggestedWorkflows = [];
    [ObservableProperty] private bool                                      _hasWorkflows;
    [ObservableProperty] private string                                    _workflowCountLabel = string.Empty;

    // ── Selected workflow detail ───────────────────────────────────────────────

    [ObservableProperty] private WorkflowViewModel? _selectedWorkflow;
    [ObservableProperty] private bool                _hasSelectedWorkflow;

    // ── Evidence chain viewer ─────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<EvidenceNodeViewModel> _evidenceNodes = [];
    [ObservableProperty] private bool                                          _hasEvidenceNodes;
    [ObservableProperty] private string                                        _evidenceChainLabel = string.Empty;

    // ── Derived visibility properties ─────────────────────────────────────────

    public string LoadingVisibility         => IsLoading ? "Visible" : "Collapsed";
    public string ContentVisibility         => !IsLoading && !HasError ? "Visible" : "Collapsed";
    public string ErrorVisibility           => HasError ? "Visible" : "Collapsed";
    public string NoGraphVisibility         => !IsLoading && !HasEvidenceGraph ? "Visible" : "Collapsed";
    public string GraphVisibility           => HasEvidenceGraph ? "Visible" : "Collapsed";
    public string NoRootCausesVisibility    => HasEvidenceGraph && !HasRootCauses ? "Visible" : "Collapsed";
    public string RootCausesVisibility      => HasRootCauses ? "Visible" : "Collapsed";
    public string PrimaryDetailVisibility   => HasPrimaryRootCause ? "Visible" : "Collapsed";
    public string WorkflowsVisibility       => HasWorkflows ? "Visible" : "Collapsed";
    public string NoWorkflowsVisibility     => HasEvidenceGraph && !HasWorkflows ? "Visible" : "Collapsed";
    public string WorkflowDetailVisibility  => HasSelectedWorkflow ? "Visible" : "Collapsed";
    public string EvidenceChainVisibility   => HasEvidenceNodes ? "Visible" : "Collapsed";

    // ── Constructor ───────────────────────────────────────────────────────────

    public InvestigationViewModel(
        IOperationalEvidenceGraph   evidenceGraph,
        IRootCauseAnalysisEngine    rootCauseEngine,
        IEvidenceExplanationService explanationService,
        IOperationalWorkflowEngine  workflowEngine)
    {
        _evidenceGraph      = evidenceGraph;
        _rootCauseEngine    = rootCauseEngine;
        _explanationService = explanationService;
        _workflowEngine     = workflowEngine;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BuildInvestigationAsync()
    {
        IsLoading      = true;
        HasError       = false;
        LoadingMessage = "Building evidence graph…";
        NotifyDerivedVisibility();

        try
        {
            // Step 1: Build evidence graph asynchronously.
            // ConfigureAwait(true): all subsequent steps (LoadingMessage updates,
            // AnalyzeRootCauses, ApplyResults) write to [ObservableProperty] fields and
            // ObservableCollections. WinUI x:Bind bindings crash when PropertyChanged
            // fires from a non-UI thread. AnalyzeRootCauses / GetSuggestedWorkflows
            // are fast in-memory operations — running them on the UI thread is safe.
            var graph = await _evidenceGraph.BuildGraphAsync().ConfigureAwait(true);

            LoadingMessage = "Analyzing root causes…";

            // Step 2: Analyze root causes
            var candidates = _rootCauseEngine.AnalyzeRootCauses(graph, maxCandidates: 5);

            LoadingMessage = "Generating investigation report…";

            // Step 3: Build workflows
            var workflows = _workflowEngine.GetSuggestedWorkflows(candidates, maxWorkflows: 3);

            // Step 4: Apply results to ViewModel (already on UI thread)
            ApplyResults(graph, candidates, workflows);
        }
        catch (OperationCanceledException)
        {
            // Swallow — page was navigated away during analysis
        }
        catch (Exception ex)
        {
            HasError     = true;
            ErrorMessage = $"Investigation failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            NotifyDerivedVisibility();
        }
    }

    [RelayCommand]
    private void SelectWorkflow(WorkflowViewModel? vm)
    {
        SelectedWorkflow    = vm;
        HasSelectedWorkflow = vm is not null;
        OnPropertyChanged(nameof(WorkflowDetailVisibility));
    }

    [RelayCommand]
    private void SelectRootCause(RootCauseCandidateViewModel? vm)
    {
        PrimaryRootCause    = vm;
        HasPrimaryRootCause = vm is not null;

        if (vm is not null)
        {
            PrimaryHeadline   = vm.ConfidenceLabel + ": " + vm.CauseTitle;
            PrimaryWhyMatters = vm.Explanation;
            PrimaryConfidence = vm.ConfidenceLabel + " (" + vm.ConfidencePercent + "%)";

            PrimaryEvidence.Clear();
            foreach (var item in vm.SupportingEvidence)
                PrimaryEvidence.Add(item);

            // Load connected evidence chain
            var chain = _evidenceGraph.GetConnectedEvidence(vm.RootNodeId);
            LoadEvidenceChain(chain);
        }

        NotifyDerivedVisibility();
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void ApplyResults(
        EvidenceChain                      graph,
        IReadOnlyList<RootCauseCandidate>  candidates,
        IReadOnlyList<OperationalWorkflow> workflows)
    {
        // Graph summary
        EvidenceNodeCount = graph.Nodes.Count;
        EvidenceLinkCount = graph.Edges.Count;
        GraphBuiltAtLabel = $"Updated {FormatAge(DateTimeOffset.Now - graph.ComputedAt)}";
        HasEvidenceGraph  = graph.Nodes.Count > 0;

        // Root causes
        RootCauses.Clear();
        foreach (var c in candidates)
        {
            var chain  = _evidenceGraph.GetConnectedEvidence(c.SupportingEvidence.Count > 0
                ? "insight:" + c.AffectedSystems.FirstOrDefault() ?? string.Empty
                : graph.RootNodeId);
            var expl   = _explanationService.ExplainCandidate(c, chain);
            RootCauses.Add(new RootCauseCandidateViewModel(c, expl, graph.RootNodeId));
        }

        HasRootCauses     = RootCauses.Count > 0;
        RootCauseCountLabel = HasRootCauses
            ? $"{RootCauses.Count} probable root cause{(RootCauses.Count == 1 ? "" : "s")} identified"
            : "No root causes identified";

        // Auto-select primary
        if (RootCauses.Count > 0)
            SelectRootCauseCommand.Execute(RootCauses[0]);

        // Workflows
        SuggestedWorkflows.Clear();
        foreach (var wf in workflows)
            SuggestedWorkflows.Add(new WorkflowViewModel(wf));

        HasWorkflows      = SuggestedWorkflows.Count > 0;
        WorkflowCountLabel = HasWorkflows
            ? $"{SuggestedWorkflows.Count} guided workflow{(SuggestedWorkflows.Count == 1 ? "" : "s")} available"
            : "No workflows available";

        if (SuggestedWorkflows.Count > 0)
            SelectWorkflowCommand.Execute(SuggestedWorkflows[0]);

        NotifyDerivedVisibility();
    }

    private void LoadEvidenceChain(EvidenceChain chain)
    {
        EvidenceNodes.Clear();
        foreach (var node in chain.Nodes.OrderByDescending(n => n.Confidence))
            EvidenceNodes.Add(new EvidenceNodeViewModel(node));

        HasEvidenceNodes   = EvidenceNodes.Count > 0;
        EvidenceChainLabel = HasEvidenceNodes
            ? $"{EvidenceNodes.Count} evidence item{(EvidenceNodes.Count == 1 ? "" : "s")} in chain"
            : "No connected evidence";

        OnPropertyChanged(nameof(EvidenceChainVisibility));
    }

    private void NotifyDerivedVisibility()
    {
        OnPropertyChanged(nameof(LoadingVisibility));
        OnPropertyChanged(nameof(ContentVisibility));
        OnPropertyChanged(nameof(ErrorVisibility));
        OnPropertyChanged(nameof(NoGraphVisibility));
        OnPropertyChanged(nameof(GraphVisibility));
        OnPropertyChanged(nameof(NoRootCausesVisibility));
        OnPropertyChanged(nameof(RootCausesVisibility));
        OnPropertyChanged(nameof(PrimaryDetailVisibility));
        OnPropertyChanged(nameof(WorkflowsVisibility));
        OnPropertyChanged(nameof(NoWorkflowsVisibility));
        OnPropertyChanged(nameof(WorkflowDetailVisibility));
        OnPropertyChanged(nameof(EvidenceChainVisibility));
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 5)  return "just now";
        if (age.TotalMinutes < 1)  return $"{(int)age.TotalSeconds}s ago";
        if (age.TotalHours < 1)    return $"{(int)age.TotalMinutes}m ago";
        return $"{(int)age.TotalHours}h ago";
    }
}

// ── Presentation wrappers ─────────────────────────────────────────────────────

/// <summary>Presentation wrapper for a <see cref="RootCauseCandidate"/>.</summary>
public sealed class RootCauseCandidateViewModel
{
    public string CauseTitle         { get; }
    public string Explanation        { get; }
    public string ConfidenceLabel    { get; }
    public string ConfidenceColor    { get; }
    public int    ConfidencePercent  { get; }
    public string AffectedSystems    { get; }
    public string RootNodeId         { get; }

    // From EvidenceExplanation
    public string Headline           { get; }
    public string WhyItMatters       { get; }
    public string LikelyOutcome      { get; }
    public string SuggestedNextStep  { get; }
    public string ConfidenceSummary  { get; }

    public IReadOnlyList<string> SupportingEvidence { get; }

    public RootCauseCandidateViewModel(
        RootCauseCandidate  candidate,
        EvidenceExplanation explanation,
        string              rootNodeId)
    {
        CauseTitle        = candidate.CauseTitle;
        Explanation       = candidate.Explanation;
        ConfidenceLabel   = candidate.ConfidenceLabel;
        ConfidenceColor   = candidate.ConfidenceColor;
        ConfidencePercent = (int)Math.Round(candidate.Confidence * 100);
        AffectedSystems   = candidate.AffectedSystems.Count > 0
            ? string.Join(", ", candidate.AffectedSystems)
            : "General";
        RootNodeId        = rootNodeId;
        SupportingEvidence = candidate.SupportingEvidence;

        Headline          = explanation.Headline;
        WhyItMatters      = explanation.WhyItMatters;
        LikelyOutcome     = explanation.LikelyOutcome;
        SuggestedNextStep = explanation.SuggestedNextStep;
        ConfidenceSummary = explanation.ConfidenceSummary;
    }
}

/// <summary>Presentation wrapper for an <see cref="OperationalWorkflow"/>.</summary>
public sealed class WorkflowViewModel
{
    public string                          Id                    { get; }
    public string                          Title                 { get; }
    public string                          ProblemSummary        { get; }
    public string                          EvidenceSummary       { get; }
    public string                          RiskSummary           { get; }
    public string                          ExpectedOutcome       { get; }
    public string                          PriorityLabel         { get; }
    public string                          PriorityColor         { get; }
    public string                          PriorityBgColor       { get; }
    public string                          SuccessRateLabel      { get; }
    public string                          SuccessRateColor      { get; }
    public string                          StepCountLabel        { get; }
    public IReadOnlyList<WorkflowStep>     Steps                 { get; }
    public IReadOnlyList<string>           FallbackActions       { get; }

    public WorkflowViewModel(OperationalWorkflow wf)
    {
        Id             = wf.Id;
        Title          = wf.Title;
        ProblemSummary = wf.ProblemSummary;
        EvidenceSummary = wf.EvidenceSummary;
        RiskSummary    = wf.RiskSummary;
        ExpectedOutcome = wf.ExpectedOutcome;
        PriorityLabel  = wf.PriorityLabel;
        PriorityColor  = wf.PriorityColor;
        PriorityBgColor = wf.PriorityBgColor;
        SuccessRateLabel = wf.SuccessRateLabel;
        SuccessRateColor = wf.SuccessRateColor;
        StepCountLabel = wf.StepCountLabel;
        Steps          = wf.Steps;
        FallbackActions = wf.FallbackActions;
    }
}

/// <summary>Presentation wrapper for an <see cref="EvidenceNode"/>.</summary>
public sealed class EvidenceNodeViewModel
{
    public string NodeType       { get; }
    public string Title          { get; }
    public string Summary        { get; }
    public string ConfidenceLabel{ get; }
    public string NodeGlyph      { get; }
    public string NodeColor      { get; }
    public string AgeLabe        { get; }

    public EvidenceNodeViewModel(EvidenceNode node)
    {
        NodeType        = node.NodeType.ToString();
        Title           = node.Title;
        Summary         = node.Summary;
        ConfidenceLabel = node.ConfidenceLabel;
        NodeGlyph       = node.NodeGlyph;
        NodeColor       = node.NodeColor;
        var age         = DateTimeOffset.Now - node.CreatedAt;
        AgeLabe = age.TotalSeconds < 5 ? "just now"
                : age.TotalMinutes < 1 ? $"{(int)age.TotalSeconds}s ago"
                : age.TotalHours < 1   ? $"{(int)age.TotalMinutes}m ago"
                : $"{(int)age.TotalHours}h ago";
    }
}
