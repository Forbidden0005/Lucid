using Lucid.Services.History;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lucid.ViewModels;

/// <summary>
/// Immutable display-layer wrapper for a persisted <see cref="OperationRecord"/>.
///
/// No INotifyPropertyChanged — OperationRecord is a sealed record with init-only
/// properties. History items are replaced in bulk when the list refreshes, so the
/// outer ItemsRepeater binding drives all updates.
///
/// Static brush palette:
///   One SolidColorBrush per outcome tier, allocated at class init time and
///   shared across every card. Zero allocations per history load.
/// </summary>
public sealed class HistoryRecordViewModel
{
    // ── Static brush palette ── one allocation per tier, ever ─────────────────

    private static readonly SolidColorBrush s_successBrush = Mk(255,  87, 214, 141); // Green  #57D68D
    private static readonly SolidColorBrush s_failureBrush = Mk(255, 255, 107, 107); // Red    #FF6B6B
    private static readonly SolidColorBrush s_infoBrush    = Mk(255,  78, 161, 255); // Blue   #4EA1FF
    private static readonly SolidColorBrush s_subtleBrush  = Mk(160, 180, 180, 180); // Muted gray

    private static readonly SolidColorBrush s_successBg    = Mk(0x26,  87, 214, 141);
    private static readonly SolidColorBrush s_failureBg    = Mk(0x26, 255, 107, 107);
    private static readonly SolidColorBrush s_infoBg       = Mk(0x26,  78, 161, 255);
    private static readonly SolidColorBrush s_subtleBg     = Mk(0x0A, 255, 255, 255);

    private static SolidColorBrush Mk(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // ── Backing record ────────────────────────────────────────────────────────

    private readonly OperationRecord _record;

    public HistoryRecordViewModel(OperationRecord record) => _record = record;

    // ── Identity ──────────────────────────────────────────────────────────────

    public string RecordId    => _record.Id;
    public string ActionId    => _record.ActionId;
    public string ActionTitle => _record.ActionTitle;

    // ── Type flags ────────────────────────────────────────────────────────────

    public bool IsDryRun      => _record.IsDryRun;
    public bool IsRollback    => _record.IsRollback;
    public bool IsSuccess     => _record.IsSuccess;
    public bool WasRolledBack => _record.WasRolledBack;

    // ── Timing ────────────────────────────────────────────────────────────────

    /// <summary>Formatted local timestamp, e.g. "May 17, 2:34 PM".</summary>
    public string TimestampText =>
        _record.ExecutedAt.LocalDateTime.ToString("MMM d, h:mm tt");

    /// <summary>Wall-clock duration as a human-readable string.</summary>
    public string DurationText
    {
        get
        {
            var ms = _record.DurationMs;
            if (ms < 1_000)    return $"{ms} ms";
            if (ms < 60_000)   return $"{ms / 1000.0:F1}s";
            return $"{ms / 60_000}m {(ms % 60_000) / 1000}s";
        }
    }

    /// <summary>Age of the record as a human-readable relative string.</summary>
    public string RelativeTime
    {
        get
        {
            var age = DateTimeOffset.Now - _record.ExecutedAt;
            if (age.TotalSeconds < 30)  return "just now";
            if (age.TotalMinutes < 1)   return $"{(int)age.TotalSeconds}s ago";
            if (age.TotalMinutes < 60)  return $"{(int)age.TotalMinutes} min ago";
            if (age.TotalHours   < 24)  return $"{(int)age.TotalHours}h ago";
            return $"{(int)age.TotalDays}d ago";
        }
    }

    // ── Status display ────────────────────────────────────────────────────────

    /// <summary>Short outcome label shown in the status badge.</summary>
    public string StatusLabel
    {
        get
        {
            if (IsRollback)             return "Rolled Back";
            if (IsDryRun)               return "Preview";
            if (IsSuccess && WasRolledBack) return "Undone";
            if (IsSuccess)              return "Success";
            return _record.Status;
        }
    }

    /// <summary>Short type label (Action / Preview / Rollback).</summary>
    public string TypeLabel
    {
        get
        {
            if (IsRollback) return "Rollback";
            if (IsDryRun)   return "Preview";
            return "Action";
        }
    }

    /// <summary>
    /// Segoe MDL2 glyph for the status icon circle.
    ///   ✓ (E73E) — success
    ///   ↩ (E72C) — rolled back / rollback
    ///   ⓘ (E946) — dry-run preview
    ///   ✗ (EA39) — failed
    /// </summary>
    public string StatusGlyph
    {
        get
        {
            if (IsRollback || WasRolledBack) return ""; // Undo arrow
            if (IsDryRun)                    return ""; // Preview binoculars
            if (IsSuccess)                   return ""; // Checkmark
            return "";                                  // Error X
        }
    }

    public SolidColorBrush StatusBrush
    {
        get
        {
            if (IsRollback || WasRolledBack) return s_infoBrush;
            if (IsDryRun)                    return s_infoBrush;
            if (IsSuccess)                   return s_successBrush;
            return s_failureBrush;
        }
    }

    public SolidColorBrush StatusBackground
    {
        get
        {
            if (IsRollback || WasRolledBack) return s_infoBg;
            if (IsDryRun)                    return s_infoBg;
            if (IsSuccess)                   return s_successBg;
            return s_failureBg;
        }
    }

    // ── Outcome message ───────────────────────────────────────────────────────

    public string Message       => _record.Message;
    public int    TotalLogLines => _record.TotalLogEntries;

    // ── Log summary ───────────────────────────────────────────────────────────

    public bool HasWarnings => _record.Warnings.Count > 0;
    public bool HasErrors   => _record.Errors.Count   > 0;

    public Visibility WarningVisibility => HasWarnings ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility   => HasErrors   ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>First three warning messages joined for the chip label.</summary>
    public string WarningsText => string.Join(" · ", _record.Warnings.Take(2));

    /// <summary>First three error messages joined for the chip label.</summary>
    public string ErrorsText => string.Join(" · ", _record.Errors.Take(2));

    // ── Rollback state ────────────────────────────────────────────────────────

    /// <summary>
    /// Badge shown when a successful forward-execution was later rolled back.
    /// Distinct from <see cref="IsRollback"/> (which marks the rollback record itself).
    /// </summary>
    public Visibility RolledBackBadgeVisibility =>
        WasRolledBack && !IsRollback ? Visibility.Visible : Visibility.Collapsed;
}
