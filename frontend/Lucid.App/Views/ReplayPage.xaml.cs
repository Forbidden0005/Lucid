using Lucid.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Views;

/// <summary>
/// Operational Replay workspace — forensic historical analysis page.
///
/// Code-behind responsibilities:
///   • Set DataContext to ReplayViewModel wired to the live replay service.
///   • Handle event-marker chip clicks (seeking via ViewModel.SeekToOffset).
///
/// All business logic lives in ReplayViewModel / IOperationalReplayService.
/// The page is a pure display surface with a single non-trivial handler.
/// </summary>
public sealed partial class ReplayPage : Page
{
    private ReplayViewModel? _vm;

    public ReplayPage()
    {
        InitializeComponent();

        _vm = new ReplayViewModel(AppServices.ReplayService);
        DataContext = _vm;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Nothing to animate on load — the ViewModel handles the async load internally.
    }

    /// <summary>
    /// Called when the user clicks an event marker chip in the event rail.
    /// Reads OffsetSeconds from Button.Tag and seeks the cursor to that position.
    /// </summary>
    private void EventMarker_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is Button { Tag: double offsetSeconds })
        {
            _vm.SeekToOffset(offsetSeconds);
        }
    }
}
