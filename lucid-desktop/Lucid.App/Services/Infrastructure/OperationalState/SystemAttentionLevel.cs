namespace Lucid.Services.Infrastructure.OperationalState;

/// <summary>
/// The degree of attention the platform is requesting from the user.
///
/// Computed by <see cref="IOperationalStateCoordinator"/> from active
/// <see cref="RuntimeCondition"/> flags and service health state.
/// Used by the UI to decide notification prominence and badge display.
/// </summary>
public enum SystemAttentionLevel
{
    /// <summary>
    /// System is healthy and operating normally. No action needed.
    /// UI: no badge, ambient status bar only.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Platform has informational findings worth reviewing but nothing urgent.
    /// UI: subtle badge or ambient indicator.
    /// </summary>
    Advisory = 1,

    /// <summary>
    /// One or more active insights require user attention in the near term.
    /// UI: visible badge with count.
    /// </summary>
    ActionRequired = 2,

    /// <summary>
    /// A critical condition is active (thermal runaway, disk exhaustion, workflow failure).
    /// UI: prominent badge, possible toast notification.
    /// </summary>
    Urgent = 3,
}
