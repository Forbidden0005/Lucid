using CommunityToolkit.Mvvm.DependencyInjection;
using Lucid.Services.Reasoning;
using Lucid.Services.Workflow;
using Lucid.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Views;

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
        ViewModel = Ioc.Default.GetRequiredService<InvestigationViewModel>();

        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.BuildInvestigationCommand.ExecuteAsync(null);
        };
    }
}
