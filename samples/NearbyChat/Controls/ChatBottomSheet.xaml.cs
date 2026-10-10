using NearbyChat.ViewModels;
using Plugin.Maui.BottomSheet;

namespace NearbyChat.Controls;

public partial class ChatBottomSheet : BottomSheet
{
    /// <summary>
    /// The largest share of the screen the sheet may cover, so the page's Back button and the
    /// peer card stay visible above it. The message list scrolls inside the rest.
    /// </summary>
    const double MaxScreenShare = 0.52;

    /// <summary>
    /// How long the list gets to rebind a failed bubble at its new height before it scrolls to it.
    /// Scrolling sooner aims at the old cell and stops short.
    /// </summary>
    static readonly TimeSpan s_relayoutDelay = TimeSpan.FromMilliseconds(100);

    CollectionView? _chatList;

    public ChatBottomSheet(ChatViewModel viewModel, IDeviceDisplay deviceDisplay)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(deviceDisplay);

        BindingContext = viewModel;
        InitializeComponent();

        var display = deviceDisplay.MainDisplayInfo;
        ContainerView.MaximumHeightRequest = display.Height / display.Density * MaxScreenShare;

        viewModel.DeliveryFailed += OnDeliveryFailed;
        Opening += (_, _) => SheetScrim.Opening();
    }

    void OnChatListLoaded(object? sender, EventArgs e) => _chatList = sender as CollectionView;

    void OnDeliveryFailed(object? sender, ChatMessageViewModel message)
        => _chatList?.Dispatcher.DispatchDelayed(
            s_relayoutDelay,
            () => _chatList?.ScrollTo(message, position: ScrollToPosition.End, animate: true));
}
