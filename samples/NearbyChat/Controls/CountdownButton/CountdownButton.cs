using System.Windows.Input;

namespace NearbyChat.Controls;

/// <summary>
/// A pill-shaped button whose outer ring counts down: while an offer window is open the ring
/// traces the remaining fraction of the pill's perimeter, clockwise from top-center.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="TemplatedView"/> shell whose template holds a <see cref="GraphicsView"/> (<c>PART_Canvas</c>) and a
/// <see cref="Label"/> (<c>PART_Label</c>). Every state or appearance change runs one
/// pipeline — <see cref="ResolveVisualState"/> → <see cref="ApplyDrawableState"/> →
/// <see cref="OnVisualStateChanged"/> — while the VSM only moves when the resolved state
/// actually changes. <see cref="Progress"/> deliberately bypasses that pipeline: on each
/// animation frame it writes the drawable's progress and invalidates the canvas, nothing more.
/// </para>
/// <para>
/// The countdown is one committed <see cref="Animation"/> over the remaining window: the
/// platform drives it per frame, each frame invalidates a single canvas, and no layout pass
/// runs.
/// </para>
/// </remarks>
public class CountdownButton : TemplatedView
{
    const string AnimationName = nameof(CountdownButton);
    const string PulseAnimationName = AnimationName + ".Pulse";
    const double PulseRest = 0.5d;
    const double DisabledOpacity = 0.5d;

    Grid? _root;
    GraphicsView? _canvas;
    Label? _label;

    readonly CountdownButtonDrawable _defaultDrawable = new();
    VisualButtonState _lastVisualState;
    CountdownUrgency _urgency;

    /// <summary>Period the pulse is currently committed at; <see cref="TimeSpan.Zero"/> when stopped.</summary>
    TimeSpan _pulsePeriod;

    public CountdownButton()
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
    }

    /// <summary>Raised when a countdown runs to zero (not when it is cancelled).</summary>
    public event EventHandler? CountdownFinished;

    // ── Bindable properties ───────────────────────────────────────────────

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(CountdownButton), string.Empty,
        propertyChanged: (b, _, _) => ((CountdownButton)b).SyncLabel());

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(CountdownButton));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(CountdownButton));

    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress), typeof(double), typeof(CountdownButton), 0d,
        propertyChanged: (b, _, n) => ((CountdownButton)b).PushProgress((double)n));

    public static readonly BindableProperty IsCountingProperty = BindableProperty.Create(
        nameof(IsCounting), typeof(bool), typeof(CountdownButton), false,
        propertyChanged: (b, _, _) => ((CountdownButton)b).UpdateVisualState());

    public static readonly BindableProperty IsExpiredProperty = BindableProperty.Create(
        nameof(IsExpired), typeof(bool), typeof(CountdownButton), false,
        propertyChanged: (b, _, _) => ((CountdownButton)b).UpdateVisualState());

    public static readonly BindableProperty StateSelectorProperty = BindableProperty.Create(
        nameof(StateSelector), typeof(VisualButtonStateSelector), typeof(CountdownButton),
        VisualButtonStateSelector.Default,
        propertyChanged: (b, _, _) => ((CountdownButton)b).UpdateVisualState());

    public static readonly BindableProperty DrawableProperty = BindableProperty.Create(
        nameof(Drawable), typeof(CountdownButtonDrawable), typeof(CountdownButton),
        defaultValueCreator: b => ((CountdownButton)b)._defaultDrawable,
        propertyChanged: OnDrawableChanged);

    // Appearance. Plain bindables so a Style can set them with AppThemeBinding; any change
    // re-runs the pipeline, which is how a theme flip reaches the drawable.
    public static readonly BindableProperty FillColorProperty = BindableProperty.Create(
        nameof(FillColor), typeof(Color), typeof(CountdownButton), Colors.Transparent,
        propertyChanged: (b, _, _) => ((CountdownButton)b).UpdateVisualState());

    public static readonly BindableProperty RingColorProperty = BindableProperty.Create(
        nameof(RingColor), typeof(Color), typeof(CountdownButton), Colors.Transparent,
        propertyChanged: (b, _, _) => ((CountdownButton)b).UpdateVisualState());

    /// <summary>Ring color once <see cref="Progress"/> falls to <see cref="CautionThreshold"/> or below.</summary>
    public static readonly BindableProperty CautionRingColorProperty = BindableProperty.Create(
        nameof(CautionRingColor), typeof(Color), typeof(CountdownButton), Colors.Transparent,
        propertyChanged: (b, _, _) => ((CountdownButton)b).UpdateVisualState());

    /// <summary>Ring color once <see cref="Progress"/> falls to <see cref="WarningThreshold"/> or below.</summary>
    public static readonly BindableProperty WarningRingColorProperty = BindableProperty.Create(
        nameof(WarningRingColor), typeof(Color), typeof(CountdownButton), Colors.Transparent,
        propertyChanged: (b, _, _) => ((CountdownButton)b).UpdateVisualState());

    public static readonly BindableProperty CautionThresholdProperty = BindableProperty.Create(
        nameof(CautionThreshold), typeof(double), typeof(CountdownButton), 0.5d,
        propertyChanged: (b, _, _) => ((CountdownButton)b).PushProgress(((CountdownButton)b).Progress));

    public static readonly BindableProperty WarningThresholdProperty = BindableProperty.Create(
        nameof(WarningThreshold), typeof(double), typeof(CountdownButton), 0.25d,
        propertyChanged: (b, _, _) => ((CountdownButton)b).PushProgress(((CountdownButton)b).Progress));

    public static readonly BindableProperty RingThicknessProperty = BindableProperty.Create(
        nameof(RingThickness), typeof(double), typeof(CountdownButton), 2d,
        propertyChanged: (b, _, _) => ((CountdownButton)b).UpdateVisualState());

    /// <summary>
    /// One full light-dark-light cycle of the fill in the warning band. Set to zero to opt out of
    /// the pulse entirely. The caution band does not pulse — only its ring and colors change.
    /// </summary>
    public static readonly BindableProperty WarningPulsePeriodProperty = BindableProperty.Create(
        nameof(WarningPulsePeriod), typeof(TimeSpan), typeof(CountdownButton), TimeSpan.FromMilliseconds(1400),
        propertyChanged: (b, _, _) => ((CountdownButton)b).SyncPulse());

    /// <summary>Outline drawn in the expired state, where there is no fill to carry the shape.</summary>
    public static readonly BindableProperty ExpiredStrokeColorProperty = BindableProperty.Create(
        nameof(ExpiredStrokeColor), typeof(Color), typeof(CountdownButton), Colors.Transparent,
        propertyChanged: (b, _, _) => ((CountdownButton)b).UpdateVisualState());

    /// <summary>Caption color in the expired state.</summary>
    public static readonly BindableProperty ExpiredTextColorProperty = BindableProperty.Create(
        nameof(ExpiredTextColor), typeof(Color), typeof(CountdownButton), Colors.Gray,
        propertyChanged: (b, _, _) => ((CountdownButton)b).UpdateVisualState());

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(CountdownButton), Colors.White,
        propertyChanged: (b, _, _) => ((CountdownButton)b).SyncLabel());

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(CountdownButton), 16d);

    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(
        nameof(FontAttributes), typeof(FontAttributes), typeof(CountdownButton), FontAttributes.Bold);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Remaining fraction of the countdown, 1 → 0. Drawn only while <see cref="IsCounting"/>.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool IsCounting
    {
        get => (bool)GetValue(IsCountingProperty);
        set => SetValue(IsCountingProperty, value);
    }

    /// <summary>
    /// Whether the window ran out. Held true for as long as the row wants the outcome on screen;
    /// outranks every other state.
    /// </summary>
    public bool IsExpired
    {
        get => (bool)GetValue(IsExpiredProperty);
        set => SetValue(IsExpiredProperty, value);
    }

    public VisualButtonStateSelector StateSelector
    {
        get => (VisualButtonStateSelector)GetValue(StateSelectorProperty);
        set => SetValue(StateSelectorProperty, value);
    }

    /// <summary>
    /// Paints the pill and ring. Subclass <see cref="CountdownButtonDrawable"/> and override
    /// <c>Draw</c> or <c>ApplyState</c> to customise either half.
    /// </summary>
    public CountdownButtonDrawable Drawable
    {
        get => (CountdownButtonDrawable)GetValue(DrawableProperty);
        set => SetValue(DrawableProperty, value);
    }

    public Color FillColor
    {
        get => (Color)GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    /// <summary>Ring color while counting. The ring is not drawn in any other state.</summary>
    public Color RingColor
    {
        get => (Color)GetValue(RingColorProperty);
        set => SetValue(RingColorProperty, value);
    }

    /// <summary>Ring color between the caution and warning thresholds.</summary>
    public Color CautionRingColor
    {
        get => (Color)GetValue(CautionRingColorProperty);
        set => SetValue(CautionRingColorProperty, value);
    }

    public Color WarningRingColor
    {
        get => (Color)GetValue(WarningRingColorProperty);
        set => SetValue(WarningRingColorProperty, value);
    }

    /// <summary>Remaining fraction at or below which the ring switches to <see cref="CautionRingColor"/>.</summary>
    public double CautionThreshold
    {
        get => (double)GetValue(CautionThresholdProperty);
        set => SetValue(CautionThresholdProperty, value);
    }

    /// <summary>Remaining fraction at or below which the ring switches to <see cref="WarningRingColor"/>.</summary>
    public double WarningThreshold
    {
        get => (double)GetValue(WarningThresholdProperty);
        set => SetValue(WarningThresholdProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public Color ExpiredStrokeColor
    {
        get => (Color)GetValue(ExpiredStrokeColorProperty);
        set => SetValue(ExpiredStrokeColorProperty, value);
    }

    public Color ExpiredTextColor
    {
        get => (Color)GetValue(ExpiredTextColorProperty);
        set => SetValue(ExpiredTextColorProperty, value);
    }

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public TimeSpan WarningPulsePeriod
    {
        get => (TimeSpan)GetValue(WarningPulsePeriodProperty);
        set => SetValue(WarningPulsePeriodProperty, value);
    }

    [System.ComponentModel.TypeConverter(typeof(FontSizeConverter))]
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontAttributes FontAttributes
    {
        get => (FontAttributes)GetValue(FontAttributesProperty);
        set => SetValue(FontAttributesProperty, value);
    }

    // ── Countdown ─────────────────────────────────────────────────────────

    /// <summary>
    /// Starts (or restarts) the ring draining from <paramref name="from"/> to zero over
    /// <paramref name="remaining"/>.
    /// </summary>
    public void BeginCountdown(TimeSpan remaining, double from = 1d)
    {
        this.AbortAnimation(AnimationName);

        IsCounting = true;
        Progress = from;

        // A window that is already spent is a finished countdown, not a skipped one: a cell
        // recycled onto such a row has to reach the same end state as one that watched it drain.
        if (remaining <= TimeSpan.Zero)
        {
            Progress = 0d;
            IsCounting = false;
            CountdownFinished?.Invoke(this, EventArgs.Empty);
            return;
        }

        new Animation(v => Progress = v, from, 0d, Easing.Linear)
            .Commit(this, AnimationName, length: (uint)remaining.TotalMilliseconds, finished: OnAnimationFinished);
    }

    public void CancelCountdown()
    {
        this.AbortAnimation(AnimationName);
        IsCounting = false;
        Progress = 0d;
    }

    void OnAnimationFinished(double value, bool cancelled)
    {
        if (cancelled)
        {
            return;
        }

        IsCounting = false;
        Progress = 0d;
        CountdownFinished?.Invoke(this, EventArgs.Empty);
    }

    // ── Template / handler lifecycle ─────────────────────────────────────

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _root = GetTemplateChild("PART_Root") as Grid;
        _canvas = GetTemplateChild("PART_Canvas") as GraphicsView;
        _label = GetTemplateChild("PART_Label") as Label;
        _canvas?.Drawable = Drawable;

        SyncLabel();
        UpdateVisualState();
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        ArgumentNullException.ThrowIfNull(args);

        if (args.NewHandler is null)
        {
            // A popped page must not keep a per-frame animation alive.
            this.AbortAnimation(AnimationName);
            StopPulse();
        }
        else
        {
            UpdateVisualState();
        }
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(IsEnabled))
        {
            UpdateVisualState();
        }
    }

    // ── Pipeline ──────────────────────────────────────────────────────────

    void UpdateVisualState()
    {
        var state = ResolveVisualState();

        ApplyDrawableState(state);
        SyncPulse();

        if (state != _lastVisualState)
        {
            _lastVisualState = state;
            OnVisualStateChanged(state);
        }
    }

    protected virtual VisualButtonState ResolveVisualState()
        => StateSelector.Resolve(isEnabled: IsEnabled, isCounting: IsCounting, urgency: _urgency, isExpired: IsExpired);

    /// <summary>
    /// Pushes state and appearance into the drawable, tints the label to match, then repaints.
    /// Runs on every pipeline pass (state flags, colors, drawable swap, template or handler
    /// attach) — never per frame.
    /// </summary>
    protected virtual void ApplyDrawableState(VisualButtonState state)
    {
        var drawable = Drawable;

        drawable.Progress = Progress;
        drawable.ApplyState(
            state,
            FillColor,
            RingColorFor(state),
            state == VisualButtonState.Expired ? ExpiredStrokeColor : Colors.Transparent,
            (float)RingThickness);

        // Written here, not through a VisualState on the root: a template-level VSM group is the
        // root Grid's, so MAUI's own IsEnabled handling drove its "Disabled" state and, with no
        // "Normal" to return to, a recycled cell kept the dim for the next row. Expired keeps full
        // opacity because its colours carry their own alpha (8% ground, 40% text).
        _root?.Opacity = state == VisualButtonState.Disabled ? DisabledOpacity : 1d;

        SyncLabel(state);
        _canvas?.Invalidate();
    }

    /// <summary>The ring color the urgency band <paramref name="state"/> calls for.</summary>
    Color RingColorFor(VisualButtonState state)
    {
        if (state == VisualButtonState.Warning)
        {
            return WarningRingColor;
        }

        return state == VisualButtonState.Caution ? CautionRingColor : RingColor;
    }

    /// <summary>
    /// The hot path: one field write, one band compare and one invalidation per animation
    /// frame. The pipeline runs only on the frame a threshold is crossed.
    /// </summary>
    void PushProgress(double progress)
    {
        OnProgressChanged(progress);

        Drawable.Progress = progress;

        var urgency = progress <= WarningThreshold ? CountdownUrgency.Warning
            : progress <= CautionThreshold ? CountdownUrgency.Caution
            : CountdownUrgency.None;

        if (urgency != _urgency)
        {
            _urgency = urgency;
            UpdateVisualState();
            return; // UpdateVisualState already invalidated.
        }

        _canvas?.Invalidate();
    }

    /// <summary>
    /// Starts, retimes or stops the fill pulse to match the current urgency band. Called on every
    /// pipeline pass; restarts the animation only when the period it should run at actually
    /// changes, so a pass that leaves the band alone does not visibly reset the pulse.
    /// </summary>
    void SyncPulse()
    {
        // The warning band alone pulses: caution already reads as escalated through the gold ring,
        // and flashing across both bands would leave the last quarter with nothing left to escalate to.
        var period = _urgency == CountdownUrgency.Warning ? WarningPulsePeriod : TimeSpan.Zero;

        // Only pulse while the countdown is actually running: a disabled or idle button in the
        // warning band (progress left at 0) must not flash. Set the period to zero to opt a
        // button out entirely — the ring and the urgency colors still carry the signal without it.
        // No reduced-motion gate: MAUI has no cross-platform flag for it.
        // On Android the platform jumps animations to their finished state when the system setting
        // is off; iOS would need a #if IOS check on UIAccessibility.IsReduceMotionEnabled.
        if (!IsCounting || !IsEnabled || period <= TimeSpan.Zero)
        {
            StopPulse();
            return;
        }

        if (period == _pulsePeriod)
        {
            return;
        }

        this.AbortAnimation(PulseAnimationName);
        _pulsePeriod = period;

        // A round trip, not a ramp: a bare 0 → 1 animation on repeat would snap from lightest back
        // to darkest each cycle. Two halves — out and back — make the fill breathe continuously,
        // and the period covers the whole cycle. Each frame writes the drawable and invalidates,
        // the same per-frame cost as Progress, with no layout pass.
        var pulse = new Animation
        {
            { 0d, 0.5d, new Animation(PushPulse, 0d, 1d, Easing.SinInOut) },
            { 0.5d, 1d, new Animation(PushPulse, 1d, 0d, Easing.SinInOut) },
        };

        pulse.Commit(this, PulseAnimationName, length: (uint)period.TotalMilliseconds,
            repeat: () => true);
    }

    void StopPulse()
    {
        if (_pulsePeriod == TimeSpan.Zero)
        {
            return;
        }

        this.AbortAnimation(PulseAnimationName);
        _pulsePeriod = TimeSpan.Zero;
        PushPulse(PulseRest);
    }

    /// <summary>Hot path, same shape as <see cref="PushProgress"/>: one field write, one invalidation.</summary>
    void PushPulse(double pulse)
    {
        Drawable.Pulse = pulse;

        _canvas?.Invalidate();
    }

    /// <summary>
    /// Called on every countdown frame, before the drawable is written. Override to derive
    /// something from the remaining fraction; keep it cheap, this is the hot path.
    /// </summary>
    /// <param name="progress">The remaining fraction, 1 → 0.</param>
    protected virtual void OnProgressChanged(double progress)
    {
    }

    /// <summary>
    /// Runs only when the resolved state changes. Moves any VSM group declared on the button
    /// itself to the state's name; the default template declares none, and paints its own dim.
    /// </summary>
    protected virtual void OnVisualStateChanged(VisualButtonState state)
        => VisualStateManager.GoToState(this, state.Name);


    // ── Helpers ───────────────────────────────────────────────────────────

    void SyncLabel() => SyncLabel(_lastVisualState);

    /// <summary>Caption shown in place of <see cref="Text"/>, or null when Text is shown.</summary>
    string? _caption;

    /// <summary>
    /// Overrides the displayed caption without touching <see cref="Text"/>.
    /// </summary>
    /// <remarks>
    /// For a caption a subclass recomputes as the button runs. Assigning <see cref="Text"/> would
    /// set a local value on the bindable, which permanently outranks every <c>DataTrigger</c>
    /// setter on the control; this leaves that precedence chain alone. Pass null to fall back to
    /// <see cref="Text"/>.
    /// </remarks>
    /// <param name="caption">The caption to show, or null to show <see cref="Text"/>.</param>
    protected void SetCaption(string? caption)
    {
        if (string.Equals(_caption, caption, StringComparison.Ordinal))
        {
            return;
        }

        _caption = caption;
        SyncLabel();
    }

    /// <summary>
    /// Text and its color. Every state but Expired keeps <see cref="TextColor"/> — the ring carries
    /// the urgency tier on its own. The caption is never dimmed while counting: it is the label of
    /// a button with a deadline, and white at full strength is what clears 4.5:1 on the fill.
    /// </summary>
    /// <remarks>
    /// The caption deliberately does not take the ring color in the warning band. The ring is
    /// themed independently of the fill, so a restyled warning ring lands the caption on an
    /// arbitrary color over the counting fill: with the discovery side's lavender ring that is
    /// light-on-dark-purple, illegible. TextColor is the one value guaranteed to be readable
    /// against the fill in both themes.
    /// </remarks>
    void SyncLabel(VisualButtonState state)
    {
        if (_label is null)
        {
            return;
        }

        if (state == VisualButtonState.Expired)
        {
            _label.Text = _caption ?? Text;
            _label.TextColor = ExpiredTextColor;
            return;
        }

        _label.Text = _caption ?? Text;
        _label.TextColor = TextColor;
    }

    void OnTapped(object? sender, TappedEventArgs e)
    {
        if (IsEnabled && Command is { } command && command.CanExecute(CommandParameter))
        {
            command.Execute(CommandParameter);
        }
    }

    static void OnDrawableChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var button = (CountdownButton)bindable;

        if (button._canvas is not null)
        {
            button._canvas.Drawable = (IDrawable)newValue;
        }

        button.UpdateVisualState();
    }
}
