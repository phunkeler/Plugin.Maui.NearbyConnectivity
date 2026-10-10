using NearbyChat.ViewModels;

namespace NearbyChat.Controls;

/// <summary>
/// A card that fades and recedes when its row's request has lapsed, then tells the row it is done
/// so the page can drop it.
/// </summary>
/// <remarks>
/// A <see cref="Border"/> subclass rather than page code-behind, for the same reason
/// <see cref="OfferWindowButton"/> is a control: cells in a recycling
/// <see cref="CollectionView"/> have no code-behind to reach them, and the subscription has to
/// follow the binding context as cells are reused.
/// </remarks>
public sealed class ExpiringCard : Border
{
    const string AnimationName = nameof(ExpiringCard);

    /// <summary>
    /// Long enough for the eye to register the card leaving before the gap closes behind it.
    /// </summary>
    static readonly TimeSpan s_exitDuration = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// How far the card recedes as it goes. Small on purpose: enough to read as receding rather
    /// than blinking out, not so much that it reads as a separate zoom.
    /// </summary>
    const double ExitScale = 0.95d;

    NearbyDeviceViewModel? _row;

    /// <inheritdoc cref="OfferWindowButton.OnBindingContextChanged"/>
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        _row?.Leaving -= OnLeaving;
        _row = BindingContext as NearbyDeviceViewModel;
        _row?.Leaving += OnLeaving;
        Reset();
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        ArgumentNullException.ThrowIfNull(args);

        if (args.NewHandler is null)
        {
            this.AbortAnimation(AnimationName);
        }
    }

    /// <summary>
    /// Returns a recycled cell to full size and opacity. Without this a cell that last showed a
    /// departing row would come back invisible and undersized under a live one.
    /// </summary>
    void Reset()
    {
        this.AbortAnimation(AnimationName);

        Opacity = 1d;
        Scale = 1d;

        // A row already mid-exit when the cell picked it up: finish it rather than showing a card
        // that will never leave.
        if (_row is { IsLeaving: true })
        {
            _row.CompleteExit();
        }
    }

    void OnLeaving(object? sender, EventArgs e)
    {
        // Opacity and Scale only — both are compositor transforms, so no layout pass runs.
        // Animating HeightRequest would run one on every frame, inside a CollectionView already
        // doing its own cell layout, and stutter. The gap closes when the row is removed, on the
        // handler's own native animation.
        if (_row is not { } row)
        {
            return;
        }

        // CubicOut, not CubicIn: an exit should commit immediately and ease off, so the card reads
        // as leaving from the first frame. CubicIn hangs at full opacity and then vanishes.
        var exit = new Animation
        {
            { 0d, 1d, new Animation(v => Opacity = v, 1d, 0d, Easing.CubicOut) },
            { 0d, 1d, new Animation(v => Scale = v, 1d, ExitScale, Easing.CubicOut) },
        };

        exit.Commit(
            this,
            AnimationName,
            length: (uint)s_exitDuration.TotalMilliseconds,
            finished: (_, cancelled) =>
            {
                // Cancelled means the cell was recycled onto another row mid-fade; that row's
                // own Reset has already taken over.
                if (!cancelled)
                {
                    row.CompleteExit();
                }
            });
    }
}
