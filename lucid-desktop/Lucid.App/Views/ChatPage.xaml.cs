using Lucid.Services.Companion;
using Lucid.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using Windows.UI.Core;

namespace Lucid.Views;

/// <summary>
/// Lucid's home page — the conversation surface.
///
/// Code-behind responsibilities are deliberately thin: build the ViewModel,
/// keep the transcript scrolled to the newest message, and translate template
/// clicks into ViewModel calls. Every decision about sessions, persistence and
/// conversation state lives in <see cref="ChatWorkspaceViewModel"/>.
///
/// Page caching: <see cref="NavigationCacheMode.Required"/> keeps the live
/// conversation intact when the user visits Dashboard or Storage and comes back.
/// Without it the Frame would build a fresh page — and a fresh, empty
/// conversation — on every return trip.
///
/// Conversation isolation: the page uses <c>AppServices.HomeChat</c>, its own
/// chat service, rather than the one behind the floating companion overlay.
/// The two surfaces show different conversations, so they must not share one
/// model context — resuming a saved session here would otherwise silently
/// discard whatever the overlay was in the middle of.
/// </summary>
public sealed partial class ChatPage : Page
{
    /// <summary>Icon font for the menu entries built in code-behind.</summary>
    private static readonly FontFamily SegoeIcons = new("Segoe MDL2 Assets");

    public ChatWorkspaceViewModel ViewModel { get; }

    public ChatPage()
    {
        // Built before InitializeComponent so x:Bind can resolve it.
        var chat = new CompanionChatViewModel(
            AppServices.HomeChat,
            // No orchestrator: this surface renders no automation chips, so
            // handing it one would only create a path nothing can reach.
            orchestrator:        null,
            logger:              AppServices.Logger,
            // The page renders its own empty state around the avatar rather than
            // opening with greeting bubbles.
            seedWelcomeMessages: false);

        // Start the model's history in step with what the page is showing. This
        // matters when the Frame has evicted and rebuilt the page: the service
        // outlives the page, so without this the model would still be carrying
        // turns from a conversation no longer on screen.
        chat.BeginNewSession();

        ViewModel = new ChatWorkspaceViewModel(
            chat,
            AppServices.ChatSessions,
            AppServices.Logger);

        InitializeComponent();

        NavigationCacheMode = NavigationCacheMode.Required;

        ViewModel.Chat.Messages.CollectionChanged += (_, _) => ScrollToLatestMessage();
    }

    // ── Composer ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Enter sends; Shift+Enter inserts a newline. The composer accepts returns so
    /// a user can paste or type a multi-line description of what their PC is doing
    /// without it being submitted halfway through.
    /// </summary>
    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;

        var shiftHeld = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (shiftHeld) return;   // let the TextBox insert the newline

        e.Handled = true;
        ViewModel.Chat.SendMessageCommand.Execute(null);
        ScrollToLatestMessage();
    }

    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: QuickAction action })
            ViewModel.Chat.ExecuteQuickActionCommand.Execute(action);
    }

    // ── Session rail ──────────────────────────────────────────────────────────

    private void SessionRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ChatRailItem item })
            _ = ViewModel.OpenSessionAsync(item.Id);
    }

    /// <summary>
    /// Builds and shows the per-conversation menu. Constructed on each click so
    /// the pin entry reads "Pin" or "Unpin" according to the row's actual state.
    /// </summary>
    private void SessionMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChatRailItem item } button) return;

        var rename = new MenuFlyoutItem
        {
            Text = "Rename",
            Icon = new FontIcon { Glyph = "\uE8AC", FontFamily = SegoeIcons },
        };
        rename.Click += (_, _) => _ = RenameSessionAsync(item);

        var pin = new MenuFlyoutItem
        {
            Text = item.PinMenuLabel,
            Icon = new FontIcon { Glyph = "\uE718", FontFamily = SegoeIcons },
        };
        pin.Click += (_, _) => _ = ViewModel.TogglePinAsync(item.Id);

        var delete = new MenuFlyoutItem
        {
            Text = "Delete",
            Icon = new FontIcon { Glyph = "\uE74D", FontFamily = SegoeIcons },
        };
        delete.Click += (_, _) => _ = DeleteSessionAsync(item);

        var flyout = new MenuFlyout();
        flyout.Items.Add(rename);
        flyout.Items.Add(pin);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(delete);

        flyout.ShowAt(button);
    }

    private async Task RenameSessionAsync(ChatRailItem item)
    {
        var input = new TextBox
        {
            Text            = item.Title,
            SelectionStart  = 0,
            SelectionLength = item.Title.Length,
            PlaceholderText = "Conversation name",
        };

        var dialog = new ContentDialog
        {
            XamlRoot          = XamlRoot,
            Title             = "Rename conversation",
            Content           = input,
            PrimaryButtonText = "Rename",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RenameSessionAsync(item.Id, input.Text);
    }

    /// <summary>
    /// Deleting a conversation removes the only copy of it, so it is confirmed
    /// first — the same standard the rest of Lucid applies to anything it cannot
    /// undo for the user.
    /// </summary>
    private async Task DeleteSessionAsync(ChatRailItem item)
    {
        var dialog = new ContentDialog
        {
            XamlRoot          = XamlRoot,
            Title             = "Delete conversation?",
            Content           = $"“{item.Title}” and its messages will be removed. This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText   = "Keep",
            DefaultButton     = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteSessionAsync(item.Id);
    }

    // ── Scrolling ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pins the view to the newest message. Deferred to low priority so the
    /// ItemsRepeater has finished laying the new content out — scrolling before
    /// that lands short of the bottom on long answers.
    /// </summary>
    private void ScrollToLatestMessage()
    {
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => MessageScroller.ChangeView(null, double.MaxValue, null, true));
    }
}
