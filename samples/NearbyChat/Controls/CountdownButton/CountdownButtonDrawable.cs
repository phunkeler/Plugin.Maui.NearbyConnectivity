namespace NearbyChat.Controls;

/// <summary>
/// A filled pill with, while counting, a ring along its outer edge that traces the remaining
/// fraction clockwise from top-center.
/// Subclass and override <see cref="Draw"/> or <see cref="ApplyState"/> to customise either half.
/// </summary>
/// <remarks>
/// <see cref="CountdownButton"/> drives two hooks: <see cref="ApplyState"/> when the resolved
/// state or a color changes (rare), and <see cref="Progress"/>/<see cref="Pulse"/> on every
/// animation frame (hot). Keeping them separate is what keeps the per-frame path down to one
/// field write and one canvas invalidation.
/// </remarks>
public class CountdownButtonDrawable : IDrawable
{
    public Color FillColor { get; set; } = Colors.Transparent;
    public Color RingColor { get; set; } = Colors.Transparent;
    public float RingThickness { get; set; } = 2f;

    /// <summary>Remaining fraction of the countdown, 1 → 0. Drawn only in the counting state.</summary>
    public double Progress { get; set; }

    /// <summary>
    /// Phase of the urgency pulse: 0 is the low end of the swing, 1 the high end, and 0.5 the
    /// resting value the button holds whenever it is not pulsing. Driven per frame like
    /// <see cref="Progress"/>.
    /// </summary>
    public double Pulse { get; set; } = 0.5d;

    /// <summary>Outline drawn instead of the fill in the expired state.</summary>
    public Color StrokeColor { get; set; } = Colors.Transparent;

    /// <summary>
    /// How far a full pulse swings the fill's luminosity. The band is centered on the fill
    /// color, so the fill travels either side of the color it already had rather than only
    /// darkening.
    /// </summary>
    const float PulseFillLuminosity = 0.08f;

    /// <summary>The fill before <see cref="Pulse"/> is applied. Recomputed only in <see cref="ApplyState"/>.</summary>
    Color _baseFillColor = Colors.Transparent;

    bool _counting;

    /// <summary>
    /// Called before the canvas is invalidated whenever the state or appearance changes.
    /// The colors come from the button's bindable properties, so a <c>Style</c> with
    /// <c>AppThemeBinding</c> re-drives this on theme change.
    /// </summary>
    /// <param name="state">The visual state to draw.</param>
    /// <param name="fillColor">The pill's fill.</param>
    /// <param name="ringColor">The countdown ring's color.</param>
    /// <param name="strokeColor">
    /// Outline drawn around the pill in states that have no fill of their own — currently
    /// <see cref="VisualButtonState.Expired"/>. Transparent in every other state.
    /// </param>
    /// <param name="ringThickness">The countdown ring's stroke width.</param>
    public virtual void ApplyState(VisualButtonState state, Color fillColor, Color ringColor, Color strokeColor, float ringThickness)
    {
        ArgumentNullException.ThrowIfNull(fillColor);

        _counting = state == VisualButtonState.Counting
            || state == VisualButtonState.Caution
            || state == VisualButtonState.Warning;

        // Expired drops the fill entirely and keeps only the outline, so the row reads as spent
        // rather than as another actionable button.
        var expired = state == VisualButtonState.Expired;

        _baseFillColor = expired ? Colors.Transparent : fillColor;

        FillColor = _baseFillColor;
        RingColor = _counting ? ringColor : Colors.Transparent;
        StrokeColor = expired ? strokeColor : Colors.Transparent;
        RingThickness = ringThickness;
    }

    public virtual void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
        {
            return;
        }

        canvas.SaveState();

        bool ringActive = RingColor.Alpha > 0f;

        // While the ring is active the outer band belongs to it alone: the fill is inset by the
        // ring width so a depleted segment reveals whatever is behind the button rather than more
        // fill. At progress 1 the ring covers the whole band, so the pill's extent never jumps.
        // The half-pixel overlap hides the antialiasing seam where fill and ring meet.
        var fillRect = ringActive ? Inset(dirtyRect, RingThickness - 0.5f) : dirtyRect;

        // Pulse 0 → 1 maps to a luminosity swing of −PulseFillLuminosity → +PulseFillLuminosity,
        // centered on the counting fill. The button drives Pulse from 0.5 (the resting color)
        // rather than 0, so starting and stopping the pulse never jumps the fill.
        canvas.FillColor = _counting
            ? _baseFillColor.AddLuminosity((float)((2d * Pulse - 1d) * PulseFillLuminosity))
            : FillColor;
        canvas.FillRoundedRectangle(fillRect, Math.Min(fillRect.Width, fillRect.Height) / 2f);

        // The full pill outline, drawn where there is no fill to carry the shape. Inset by half
        // the stroke for the same reason the ring is: a centered stroke would clip at the edge.
        if (StrokeColor.Alpha > 0f)
        {
            var outline = Inset(dirtyRect, RingThickness / 2f);

            canvas.StrokeColor = StrokeColor;
            canvas.StrokeSize = RingThickness;
            canvas.DrawRoundedRectangle(outline, Math.Min(outline.Width, outline.Height) / 2f);
        }

        if (ringActive && Progress > 0d)
        {
            // Stroke centered on the band: inset by half the width so nothing is clipped.
            canvas.StrokeColor = RingColor;
            canvas.StrokeSize = RingThickness;
            canvas.StrokeLineCap = LineCap.Butt;
            canvas.DrawPath(PillPerimeter.Partial(Inset(dirtyRect, RingThickness / 2f), (float)Math.Min(Progress, 1d)));
        }

        canvas.RestoreState();
    }

    static RectF Inset(RectF rect, float by)
        => new(rect.X + by, rect.Y + by, rect.Width - 2f * by, rect.Height - 2f * by);
}
