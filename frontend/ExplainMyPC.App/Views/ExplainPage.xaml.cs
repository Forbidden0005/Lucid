using ExplainMyPC.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ExplainMyPC.Views;

/// <summary>
/// Explain My PC page — conversational system analysis with diagnostic
/// findings and actionable recommendations.
///
/// Code-behind is intentionally thin:
///   • Staggered card entrance + confidence bar fill delegated to AnimationHelper.
///   • Card hover effects (scale + border) delegated to AnimationHelper.
///   • No business logic lives here.
/// </summary>
public sealed partial class ExplainPage : Page
{
    public ExplainPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // All named cards slide up and fade in with 80 ms staggered delay.
        Border[] cards =
        [
            CardSummary,
            CardFinding1, CardFinding2, CardFinding3,
            CardFinding4, CardFinding5,
            CardRec1, CardRec2, CardRec3,
        ];

        for (int i = 0; i < cards.Length; i++)
            AnimationHelper.AnimateEntrance(cards[i], delayMs: i * 80);

        // Confidence bars fill after the cards have settled on screen.
        int barDelay = cards.Length * 80 + 100;
        AnimationHelper.AnimateProgressBar(ConfBar1, 94, barDelay);
        AnimationHelper.AnimateProgressBar(ConfBar2, 89, barDelay +  60);
        AnimationHelper.AnimateProgressBar(ConfBar3, 98, barDelay + 120);
        AnimationHelper.AnimateProgressBar(ConfBar4, 97, barDelay + 180);
        AnimationHelper.AnimateProgressBar(ConfBar5, 95, barDelay + 240);
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        => AnimationHelper.PointerEntered(sender);

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        => AnimationHelper.PointerExited(sender);
}
