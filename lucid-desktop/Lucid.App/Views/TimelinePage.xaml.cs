using Lucid.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Views;

/// <summary>
/// Operational Timeline page — unified chronological event stream.
///
/// Code-behind is intentionally thin:
///   ViewModel — instantiated here; exposed as a public property so
///               XAML can bind via x:Bind.
///   Cleanup   — Page.Unloaded unsubscribes from the timeline service.
///
/// The <see cref="TimelineItemTemplateSelector"/> (nested class) routes
/// flat-list items to either the group-header template or the event-card
/// template, replacing the complexity of CollectionViewSource grouping.
/// </summary>
public sealed partial class TimelinePage : Page
{
    public TimelinePageViewModel ViewModel { get; } = new TimelinePageViewModel(AppServices.Timeline);

    public TimelinePage()
    {
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Cleanup();
    }
}

/// <summary>
/// Selects between the section-header template and the event-card template
/// for items in the flat timeline list.
///
/// Items are either <see cref="TimelineGroupHeaderViewModel"/> (section dividers)
/// or <see cref="TimelineEventViewModel"/> (event cards).
/// </summary>
public sealed class TimelineItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? EventTemplate  { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is TimelineGroupHeaderViewModel ? HeaderTemplate : EventTemplate;
}
