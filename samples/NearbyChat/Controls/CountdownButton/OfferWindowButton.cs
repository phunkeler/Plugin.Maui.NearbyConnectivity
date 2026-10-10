using System.Globalization;
using NearbyChat.ViewModels;

namespace NearbyChat.Controls;

/// <summary>
/// A <see cref="CountdownButton"/> that drives itself from the offer window on its row, so it can
/// sit in a recycling <see cref="CollectionView"/> template where no code-behind can reach it.
/// </summary>
/// <remarks>
/// The row-aware half of the control, kept separate so <see cref="CountdownButton"/> stays a plain
/// countdown with no knowledge of <see cref="NearbyDeviceViewModel"/>. It drains the same deadline
/// as <see cref="OfferWindowSeconds"/>, so badge and ring stay in step.
/// </remarks>
public sealed class OfferWindowButton : CountdownButton
{
    NearbyDeviceViewModel? _row;

    public OfferWindowButton() => CountdownFinished += OnCountdownFinished;

    /// <summary>
    /// Marks the row expired the moment the ring empties, rather than waiting for the session to
    /// report the timeout.
    /// </summary>
    /// <remarks>
    /// The two run on different clocks. The ring is committed over
    /// <see cref="NearbyDeviceViewModel.WindowRemaining"/>, measured when the view picked the row
    /// up; the session's deadline started earlier, when <c>ConnectAsync</c> was entered, and does
    /// not fire until the invite, the device change and the collection update have all gone
    /// round. Driving the expired state from the exception left the caption reading "Awaiting..."
    /// for that whole gap after the ring had visibly finished. This is the clock the user is
    /// watching, so it is the one the outcome follows.
    /// </remarks>
    void OnCountdownFinished(object? sender, EventArgs e) => _row?.MarkExpired();

    /// <remarks>
    /// A cell's binding context is replaced as the <see cref="CollectionView"/> recycles it, so the
    /// subscription follows the context rather than being taken once at construction — otherwise a
    /// recycled cell would keep counting down for the row it used to show.
    /// </remarks>
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_row is not null)
        {
            _row.WindowChanged -= OnWindowChanged;
        }

        _row = BindingContext as NearbyDeviceViewModel;

        if (_row is not null)
        {
            _row.WindowChanged += OnWindowChanged;
        }

        Restart();
    }

    void OnWindowChanged(object? sender, EventArgs e) => Restart();

    /// <summary>
    /// Caption shown while the window is open, with <c>{0}</c> replaced by the whole seconds left
    /// (for example <c>"Accept ({0}s)"</c>). Leave unset to keep <see cref="CountdownButton.Text"/>
    /// unchanged while counting.
    /// </summary>
    /// <remarks>
    /// For the side that has to answer within the window. The discovery side sets a plain
    /// <c>Text</c> instead: it is waiting on the other device, so the seconds are information, not
    /// a prompt.
    /// </remarks>
    public static readonly BindableProperty CountingTextFormatProperty = BindableProperty.Create(
        nameof(CountingTextFormat), typeof(string), typeof(OfferWindowButton));

    public string? CountingTextFormat
    {
        get => (string?)GetValue(CountingTextFormatProperty);
        set => SetValue(CountingTextFormatProperty, value);
    }

    /// <summary>Seconds currently shown, so the caption is rewritten only when it changes.</summary>
    int _shownSeconds = -1;

    /// <summary>
    /// Matches the ring to the row's current window: counting from wherever the window already is
    /// when a recycled cell picks up a handshake that is part-way through, idle when there is none.
    /// </summary>
    void Restart()
    {
        IsExpired = _row?.IsAnnouncingExpiry ?? false;
        _shownSeconds = -1;

        if (_row is not { HasWindow: true } row)
        {
            // Drop any counting caption: with no window there is nothing to count, and Text —
            // whatever the triggers currently make it — is what should show.
            SetCaption(null);
            CancelCountdown();
            return;
        }

        BeginCountdown(row.WindowRemaining, row.WindowProgress);
    }

    /// <summary>
    /// Rewrites the caption on the frame the whole-second count changes.
    /// </summary>
    /// <remarks>
    /// Driven off the ring's own progress rather than a timer of its own, so the number and the
    /// ring can never disagree, and a dropped frame corrects itself on the next one. Rounded up so
    /// the caption reads the full window the moment it opens and "1s" until it actually closes,
    /// matching <see cref="OfferWindowSeconds"/>.
    /// </remarks>
    protected override void OnProgressChanged(double progress)
    {
        base.OnProgressChanged(progress);

        if (CountingTextFormat is not { Length: > 0 } format || _row is not { HasWindow: true } row)
        {
            return;
        }

        var seconds = (int)Math.Ceiling(row.WindowRemaining.TotalSeconds);

        if (seconds == _shownSeconds)
        {
            return;
        }

        _shownSeconds = seconds;

        // SetValue on Text would write a local value, which outranks every DataTrigger setter on
        // this button from then on: the caption would be stuck counting and no later trigger —
        // the failure reason included — could ever replace it. The label is written directly
        // instead, and Text stays the trigger-owned value the button falls back to.
        SetCaption(string.Format(CultureInfo.CurrentCulture, format, seconds));
    }
}
