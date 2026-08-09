namespace Lucid.Services.Infrastructure.Events;

/// <summary>
/// Determines the order in which event handlers are invoked within a
/// subscription list. Higher-priority handlers run before lower-priority ones.
/// </summary>
public enum EventPriority
{
    /// <summary>Run after Normal handlers — suitable for cleanup or audit work.</summary>
    Low = 0,

    /// <summary>Default priority for most operational subscriptions.</summary>
    Normal = 1,

    /// <summary>Run before Normal handlers — suitable for governance or trust checks.</summary>
    High = 2,

    /// <summary>
    /// Reserved for critical infrastructure (e.g. diagnostic capture, crash containment).
    /// Should rarely be used outside the platform core.
    /// </summary>
    Critical = 3,
}
