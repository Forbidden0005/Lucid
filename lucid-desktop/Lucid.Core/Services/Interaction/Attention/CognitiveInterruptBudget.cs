using System.Threading;

namespace Lucid.Services.Interaction.Attention;

/// <summary>
/// Tracks and enforces a per-time-window interrupt budget for the Lucid UI.
///
/// The interrupt budget prevents notification storms — too many simultaneous
/// recommendations or alerts cause cognitive overload and reduce trust.
///
/// Budget rules (defaults):
///   - Max 3 "new recommendation" interrupts per 5-minute window
///   - Max 1 "urgent" interrupt per 2-minute window
///   - Budget resets after the window expires
///
/// Thread-safe: all counters use Interlocked operations.
/// </summary>
public sealed class CognitiveInterruptBudget
{
    // ── Configuration ─────────────────────────────────────────────────────────

    private readonly int      _maxInterruptsPerWindow;
    private readonly int      _maxUrgentPerWindow;
    private readonly TimeSpan _window;
    private readonly TimeSpan _urgentCooldown;

    // ── State ─────────────────────────────────────────────────────────────────

    private long _windowStartTicks;
    private long _urgentWindowStartTicks;
    private int  _interruptCount;
    private int  _urgentCount;

    public CognitiveInterruptBudget(
        int?      maxInterruptsPerWindow = null,
        int?      maxUrgentPerWindow     = null,
        TimeSpan? window                 = null,
        TimeSpan? urgentCooldown         = null)
    {
        _maxInterruptsPerWindow = maxInterruptsPerWindow ?? 3;
        _maxUrgentPerWindow     = maxUrgentPerWindow     ?? 1;
        _window                 = window          ?? TimeSpan.FromMinutes(5);
        _urgentCooldown         = urgentCooldown  ?? TimeSpan.FromMinutes(2);

        var now = DateTime.UtcNow.Ticks;
        _windowStartTicks       = now;
        _urgentWindowStartTicks = now;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when a standard interrupt is within budget and records it.
    /// Returns false when the budget is exhausted for the current window.
    /// </summary>
    public bool TryConsume(bool isUrgent = false)
    {
        ResetIfWindowExpired();

        if (isUrgent)
        {
            ResetUrgentIfExpired();
            if (Interlocked.Increment(ref _urgentCount) > _maxUrgentPerWindow)
            {
                Interlocked.Decrement(ref _urgentCount);
                return false;
            }
        }

        if (Interlocked.Increment(ref _interruptCount) > _maxInterruptsPerWindow)
        {
            Interlocked.Decrement(ref _interruptCount);
            if (isUrgent) Interlocked.Decrement(ref _urgentCount);
            return false;
        }

        return true;
    }

    /// <summary>Remaining standard interrupts in the current window.</summary>
    public int RemainingBudget =>
        Math.Max(0, _maxInterruptsPerWindow - _interruptCount);

    /// <summary>True when no more standard interrupts are allowed in this window.</summary>
    public bool IsExhausted => RemainingBudget == 0;

    // ── Private ───────────────────────────────────────────────────────────────

    private void ResetIfWindowExpired()
    {
        var windowStart = new DateTime(Interlocked.Read(ref _windowStartTicks), DateTimeKind.Utc);
        if (DateTime.UtcNow - windowStart >= _window)
        {
            Interlocked.Exchange(ref _windowStartTicks, DateTime.UtcNow.Ticks);
            Interlocked.Exchange(ref _interruptCount, 0);
        }
    }

    private void ResetUrgentIfExpired()
    {
        var urgentStart = new DateTime(
            Interlocked.Read(ref _urgentWindowStartTicks), DateTimeKind.Utc);
        if (DateTime.UtcNow - urgentStart >= _urgentCooldown)
        {
            Interlocked.Exchange(ref _urgentWindowStartTicks, DateTime.UtcNow.Ticks);
            Interlocked.Exchange(ref _urgentCount, 0);
        }
    }
}
