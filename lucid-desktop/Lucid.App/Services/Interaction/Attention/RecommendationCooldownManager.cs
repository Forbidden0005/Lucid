using System.Collections.Concurrent;
using System.Threading;

namespace Lucid.Services.Interaction.Attention;

/// <summary>
/// Tracks per-recommendation cooldown windows to prevent the same recommendation
/// from being re-surfaced too soon after it was shown.
///
/// Cooldown rules:
///   - After a recommendation is shown, it enters a cooldown window
///   - During cooldown, the recommendation is not eligible to interrupt again
///   - Cooldown duration scales with severity: shorter for urgent, longer for low-priority
///   - The user can always override by explicitly navigating to the recommendation
///
/// Thread-safe: ConcurrentDictionary-backed, no locking required.
/// </summary>
public sealed class RecommendationCooldownManager
{
    // Maps: actionKey → UTC ticks when cooldown expires
    private readonly ConcurrentDictionary<string, long> _cooldownExpiry = new();

    // Default cooldown durations by urgency level.
    private static readonly TimeSpan UrgentCooldown    = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StandardCooldown  = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LowCooldown       = TimeSpan.FromHours(2);

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the recommendation with <paramref name="actionKey"/>
    /// is eligible to interrupt (not in cooldown).
    /// </summary>
    public bool IsEligible(string actionKey)
    {
        if (!_cooldownExpiry.TryGetValue(actionKey, out var expiryTicks))
            return true; // Never shown — always eligible.

        return DateTime.UtcNow.Ticks > expiryTicks;
    }

    /// <summary>
    /// Records that a recommendation was shown and starts its cooldown window.
    /// </summary>
    public void RecordShown(string actionKey, CooldownUrgency urgency = CooldownUrgency.Standard)
    {
        var duration = urgency switch
        {
            CooldownUrgency.Urgent   => UrgentCooldown,
            CooldownUrgency.Low      => LowCooldown,
            _                        => StandardCooldown,
        };

        var expiryTicks = (DateTime.UtcNow + duration).Ticks;
        _cooldownExpiry[actionKey] = expiryTicks;
    }

    /// <summary>Clears the cooldown for a recommendation (e.g., when the user requests refresh).</summary>
    public void ClearCooldown(string actionKey) =>
        _cooldownExpiry.TryRemove(actionKey, out _);

    /// <summary>Returns the remaining cooldown duration, or TimeSpan.Zero when eligible.</summary>
    public TimeSpan RemainingCooldown(string actionKey)
    {
        if (!_cooldownExpiry.TryGetValue(actionKey, out var expiryTicks))
            return TimeSpan.Zero;

        var remaining = new DateTime(expiryTicks, DateTimeKind.Utc) - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>Total number of recommendations currently in cooldown.</summary>
    public int ActiveCooldownCount =>
        _cooldownExpiry.Count(pair => pair.Value > DateTime.UtcNow.Ticks);
}

/// <summary>Urgency classification for cooldown duration selection.</summary>
public enum CooldownUrgency
{
    /// <summary>Urgent recommendation — shorter cooldown so it can re-surface sooner.</summary>
    Urgent,

    /// <summary>Standard recommendation — default cooldown window.</summary>
    Standard,

    /// <summary>Low-priority recommendation — longer cooldown to reduce noise.</summary>
    Low,
}
