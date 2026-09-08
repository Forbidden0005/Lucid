using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Controls;

/// <summary>
/// The animated presence that represents Lucid in the chat surface.
///
/// Gives the assistant a face to speak from without pretending to be a person:
/// an abstract luminous form whose motion is tied to what Lucid is actually
/// doing. It idles calmly, and it only speeds up and spins its orbit ring while
/// a response is genuinely being generated — motion here is a status signal, not
/// decoration.
///
/// States (<see cref="State"/>, matched to VisualStates in the XAML):
///   "Idle"     — waiting for the user
///   "Thinking" — a response is being generated
///
/// The voice phase adds "Speaking" and "Listening" states, at which point the
/// core's scale is driven by the speech amplitude envelope so the avatar moves
/// with the words it is saying. Those states are deliberately not declared yet —
/// an avatar that can enter a state nothing ever triggers is just dead markup.
///
/// Unrecognised state names fall back to Idle rather than freezing the avatar.
/// </summary>
public sealed partial class CompanionAvatar : UserControl
{
    /// <summary>Default state name, also used as the fallback for unknown values.</summary>
    public const string IdleState = "Idle";

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(
            nameof(State),
            typeof(string),
            typeof(CompanionAvatar),
            new PropertyMetadata(IdleState, OnStateChanged));

    /// <summary>
    /// Current activity state. Bound to the chat ViewModel; see the class remarks
    /// for the recognised values.
    /// </summary>
    public string State
    {
        get => (string)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public CompanionAvatar()
    {
        InitializeComponent();

        // Apply the initial state once the visual tree exists — GoToState is a
        // no-op before Loaded, so a control created in the Idle state would
        // otherwise sit perfectly still until something changed it.
        Loaded += (_, _) => ApplyState(useTransitions: false);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CompanionAvatar)d).ApplyState(useTransitions: true);

    private void ApplyState(bool useTransitions)
    {
        var state = string.IsNullOrWhiteSpace(State) ? IdleState : State;

        // A state name with no matching VisualState returns false rather than
        // throwing; fall back so the avatar always has running motion.
        if (!VisualStateManager.GoToState(this, state, useTransitions))
            VisualStateManager.GoToState(this, IdleState, useTransitions);
    }
}
