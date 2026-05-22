using ExplainMyPC.Services.Reasoning;
using ExplainMyPC.Services.Workflow;
using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Views;

/// <summary>
/// Unified Investigation workspace — connects evidence, root causes, and workflows
/// into a single cohesive operational decision surface.
///
/// Code-behind is intentionally thin:
///   • ViewModel is constructed from AppServices.
///   • Page_Loaded kicks off the initial investigation build.
///   • No business logic lives here.
/// </summary>
public sealed partial class InvestigationPage : Page
{
    public InvestigationViewModel ViewModel { get; }

    public InvestigationPage()
    {
        ViewModel = new InvestigationViewModel(
            AppServices.EvidenceGraph,
            AppServices.RootCauseEngine,
            AppServices.EvidenceExplanation,
            AppServices.WorkflowEngine);

        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.BuildInvestigationCommand.ExecuteAsync(null);
        };
    }
}
