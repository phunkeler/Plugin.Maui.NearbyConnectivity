namespace NearbyChat.Controls;

/// <summary>
/// Builds the outline of a pill (a rectangle with fully rounded ends) as a path that starts at
/// top-center and runs clockwise, cut off after a fraction of the total perimeter. Drawing the
/// fraction that remains is what makes the ring read as a countdown: the gap opens at top-center
/// and grows clockwise as the fraction falls.
/// </summary>
/// <remarks>
/// An explicit segment walk rather than a dash pattern on a rounded rectangle, because where a
/// platform starts its rounded-rectangle path is not specified — Skia and CoreGraphics disagree —
/// and a dash offset would put the gap somewhere different on each platform.
/// </remarks>
public static class PillPerimeter
{
    /// <param name="rect">The pill's bounds. The stroke is centered on this outline.</param>
    /// <param name="fraction">How much of the perimeter to keep, clockwise from top-center, 0..1.</param>
    public static PathF Partial(RectF rect, float fraction)
    {
        var path = new PathF();

        if (fraction <= 0f || rect.Width <= 0f || rect.Height <= 0f)
        {
            return path;
        }

        float r = Math.Min(rect.Width, rect.Height) / 2f;
        float straight = Math.Max(0f, rect.Width - 2f * r);
        float cx = rect.Center.X;
        float top = rect.Top;
        float bottom = rect.Bottom;
        float rightStart = rect.Right - r;   // where the top edge meets the right cap
        float leftEnd = rect.Left + r;       // where the left cap meets the top edge
        float arc = MathF.PI * r;

        path.MoveTo(cx, top);

        if (fraction >= 1f)
        {
            path.LineTo(rightStart, top);
            path.AddArc(rect.Right - 2f * r, top, rect.Right, bottom, 90f, -90f, clockwise: true);
            path.LineTo(leftEnd, bottom);
            path.AddArc(rect.Left, top, rect.Left + 2f * r, bottom, 270f, 90f, clockwise: true);
            path.LineTo(cx, top);
            path.Close();
            return path;
        }

        float budget = fraction * (2f * straight + 2f * arc);

        // 1. top edge, top-center → right cap
        if (!Line(path, cx, top, rightStart, top, straight / 2f, ref budget))
        {
            return path;
        }

        // 2. right cap, sweeping clockwise from 12 o'clock toward 6 o'clock
        if (!Arc(path, rect.Right - 2f * r, top, rect.Right, bottom, 90f, arc, ref budget))
        {
            return path;
        }

        // 3. bottom edge, right → left
        if (!Line(path, rightStart, bottom, leftEnd, bottom, straight, ref budget))
        {
            return path;
        }

        // 4. left cap, sweeping clockwise from 6 o'clock toward 12 o'clock
        if (!Arc(path, rect.Left, top, rect.Left + 2f * r, bottom, 270f, arc, ref budget))
        {
            return path;
        }

        // 5. top edge, left cap → top-center
        Line(path, leftEnd, top, cx, top, straight / 2f, ref budget);
        return path;
    }

    /// <returns><see langword="true"/> when the whole segment fit and the walk should continue.</returns>
    static bool Line(PathF path, float x1, float y1, float x2, float y2, float length, ref float budget)
    {
        if (length <= 0f)
        {
            return true;
        }

        if (budget >= length)
        {
            path.LineTo(x2, y2);
            budget -= length;
            return true;
        }

        float t = budget / length;
        path.LineTo(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t);
        budget = 0f;
        return false;
    }

    /// <summary>
    /// A 180° cap. MAUI angles are degrees, 0° at 3 o'clock, counter-clockwise positive, so a
    /// clockwise sweep decreases the angle.
    /// </summary>
    static bool Arc(PathF path, float x1, float y1, float x2, float y2, float startAngle, float length, ref float budget)
    {
        if (length <= 0f)
        {
            return true;
        }

        float t = budget >= length ? 1f : budget / length;
        path.AddArc(x1, y1, x2, y2, startAngle, startAngle - 180f * t, clockwise: true);

        if (t >= 1f)
        {
            budget -= length;
            return true;
        }

        budget = 0f;
        return false;
    }
}
