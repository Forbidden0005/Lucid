namespace Lucid.ViewModels;

/// <summary>
/// Marker item for section dividers in the flat timeline list.
///
/// The <see cref="TimelinePage"/> uses a <c>DataTemplateSelector</c> to distinguish
/// between these header objects and <see cref="TimelineEventViewModel"/> items so the
/// same <c>ListView</c> can render both without <c>CollectionViewSource</c> grouping.
///
/// Instances are immutable — rebuilt entirely whenever the filter or time window changes.
/// </summary>
public sealed class TimelineGroupHeaderViewModel
{
    /// <summary>Section label shown as the group divider.</summary>
    public string Label { get; }

    /// <summary>Number of events in this section after filtering.</summary>
    public int Count { get; }

    public TimelineGroupHeaderViewModel(string label, int count)
    {
        Label = label;
        Count = count;
    }

    /// <summary>Formatted count badge — e.g. "3 events", "1 event".</summary>
    public string CountText => $"{Count} event{(Count == 1 ? "" : "s")}";
}
