using ExplainMyPC.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExplainMyPC.Views;

/// <summary>
/// Repair Center page — safe fix and recovery workflows.
///
/// Code-behind responsibilities:
///   ViewModel  — instantiated here; exposed as public property for x:Bind.
///   Callbacks  — registers the confirmation dialog and narrative callbacks
///                on each RepairCardViewModel after the page loads.
///   Cleanup    — cancels any running repairs on Unloaded.
/// </summary>
public sealed partial class RepairsPage : Page
{
    public RepairsViewModel ViewModel { get; } = new RepairsViewModel();

    public RepairsPage()
    {
        InitializeComponent();
        Loaded   += Page_Loaded;
        Unloaded += (_, _) => ViewModel.Cleanup();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Wire callbacks on every card. This must happen after the page
        // is loaded so XamlRoot is available for ContentDialog.ShowAsync.
        foreach (var card in ViewModel.RepairCards)
        {
            card.RequestConfirmationAsync = ShowConfirmDialogAsync;
            card.NotifyRepairCompleted    = NotifyNarrative;
        }
    }

    // ── Confirmation dialog ───────────────────────────────────────────────────

    private async Task<bool> ShowConfirmDialogAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title               = "Confirm Repair",
            Content             = message,
            PrimaryButtonText   = "Run It",
            CloseButtonText     = "Cancel",
            DefaultButton       = ContentDialogButton.Close,
            XamlRoot            = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    // ── Narrative bridge ──────────────────────────────────────────────────────

    private static void NotifyNarrative(
        string actionId, string displayName, bool succeeded, bool requiresRestart)
    {
        // Reach directly to the narrative engine so it can add a repair paragraph.
        if (AppServices.Narrative is Services.Narrative.OperationalNarrativeEngine engine)
            engine.NotifyRepairCompleted(actionId, displayName, succeeded, requiresRestart);
    }
}
