namespace Lucid.Services.Diagnostics.Governance;

/// <summary>
/// Immutable record of a change in the platform's trust or consent posture.
///
/// Stored in the governance audit log and published to the event bus so
/// subscribers (diagnostics page, transparency service) can surface trust
/// changes to the user.
/// </summary>
public sealed record TrustTransitionRecord(
    string    TransitionType,
    string    PreviousValue,
    string    NewValue,
    DateTime  OccurredAtUtc,
    string?   Context = null);
