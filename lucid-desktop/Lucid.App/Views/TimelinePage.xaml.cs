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
    // Migrated off the AppServices static locator (ROADMAP C4 strangler
    // template): the ViewModel factory is registered during AppServices
    // initialization and resolved through the composition seam.
    public TimelinePageViewModel ViewModel { get; } =
        App.Registry.Resolve<TimelinePageViewModel>();

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
