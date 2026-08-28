namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// Draws the dark scrim with a transparent "spotlight" hole cut into it. The hole is not a redrawn copy
/// of the target — a single path containing both the full-canvas rect and the spotlight shape is filled
/// once with <see cref="WindingMode.EvenOdd"/>, so the overlapping region (the hole) is simply never
/// painted and the real control underneath shows straight through. Porter-duff "destination-out"
/// compositing is deliberately not used, since it isn't reliably cross-platform in Microsoft.Maui.Graphics.
/// </summary>
public sealed class SpotlightDrawable : IDrawable
{
    /// <summary>Null means no active spotlight yet — draws a plain full-screen scrim with no hole.</summary>
    public SpotlightGeometry? Geometry { get; set; }

    public Color ScrimColor { get; set; } = Color.FromArgb("#B3000000");

    /// <summary>Soft blurred light traced around the hole's edge, not a solid-color border — the scrim
    /// is dark by default, so a fixed light glow reads consistently without needing to be theme-aware
    /// itself.</summary>
    public Color GlowColor { get; set; } = Colors.White.WithAlpha(0.65f);

    public float GlowRadius { get; set; } = 14;

    /// <summary>0..1, driven externally (see OnboardingOverlayView's pulse loop) to breathe the glow's
    /// spread/brightness over time — 1 is full <see cref="GlowRadius"/>/brightness, 0 is its dimmest.</summary>
    public float PulseIntensity { get; set; } = 1f;

    /// <summary>Null means no arrow drawn — set once the tooltip card's on-screen position (and hence
    /// which side its connecting arrow sits on) is known for the current step. Drawn as Microsoft.Maui.
    /// Graphics path drawing (see <see cref="BuildArrowIconPath"/>), rotated per
    /// <see cref="OnboardingArrowDirection"/>, rather than a static SVG asset, so the same path data
    /// re-orients per step with no extra assets. Filled with <see cref="GlowColor"/> — see
    /// <see cref="DrawArrow"/>.</summary>
    public (Rect Bounds, OnboardingArrowDirection Direction)? Arrow { get; set; }

    /// <summary>0..1, animated alongside the tooltip card's own fade (see OnboardingOverlayView) so the
    /// arrow appears/disappears together with the card instead of snapping to its new position mid-fade.</summary>
    public float ArrowOpacity { get; set; } = 1f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var path = new PathF();
        path.AppendRectangle(dirtyRect);

        if (Geometry is { } geometry)
        {
            var hole = new RectF((float)geometry.Bounds.X, (float)geometry.Bounds.Y, (float)geometry.Bounds.Width, (float)geometry.Bounds.Height);
            AppendHoleShape(path, geometry, hole);

            canvas.FillColor = ScrimColor;
            canvas.FillPath(path, WindingMode.EvenOdd);

            DrawGlow(canvas, geometry, hole);
        }
        else
        {
            canvas.FillColor = ScrimColor;
            canvas.FillPath(path, WindingMode.EvenOdd);
        }

        if (Arrow is { } arrow && ArrowOpacity > 0)
            DrawArrow(canvas, arrow.Bounds, arrow.Direction);
    }

    // Native bounding box of the path data below, in its own authored 512x512 SVG coordinate space —
    // computed by hand from the same coordinates (min/max over every point and control point), since
    // that's cheaper and more certain than depending on PathF exposing its own bounds. Used to center and
    // scale the icon into whatever small on-screen rect DrawArrow is given.
    private const float ArrowIconMinX = -2.538f;
    private const float ArrowIconMinY = 93.624f;
    private const float ArrowIconMaxX = 514.473f;
    private const float ArrowIconMaxY = 407.623f;
    private const float ArrowIconCenterX = (ArrowIconMinX + ArrowIconMaxX) / 2f;
    private const float ArrowIconCenterY = (ArrowIconMinY + ArrowIconMaxY) / 2f;
    private const float ArrowIconWidth = ArrowIconMaxX - ArrowIconMinX;
    private const float ArrowIconHeight = ArrowIconMaxY - ArrowIconMinY;

    private void DrawArrow(ICanvas canvas, Rect bounds, OnboardingArrowDirection direction)
    {
        var targetRect = new RectF((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);

        // Contain-fit: uniform scale so the icon's larger dimension fills the available rect without
        // distorting its aspect ratio, then center it there.
        float scale = Math.Min(targetRect.Width / ArrowIconWidth, targetRect.Height / ArrowIconHeight);
        float centerX = targetRect.Center.X;
        float centerY = targetRect.Center.Y;

        // Every point of the path (including Bezier control points) is run through this same rotate
        // (in 90-degree steps per direction, around the icon's own center) + uniform scale + translate
        // (to the target rect's center) before being handed to the path — a plain affine transform, which
        // Bezier curves are closed under, so transforming each control point independently still produces
        // the correctly-transformed curve. Kept as manual per-point math (axis swaps, no trig needed since
        // rotations are all 90-degree multiples) rather than canvas-level transform calls, so this only
        // relies on PathF's basic MoveTo/LineTo/CubicTo — no dependency on canvas Rotate/Translate/Scale
        // or PathF.Bounds actually existing/behaving as assumed.
        PointF Transform(float x, float y)
        {
            float dx = x - ArrowIconCenterX;
            float dy = y - ArrowIconCenterY;

            (float rx, float ry) = direction switch
            {
                OnboardingArrowDirection.Down => (dx, dy),
                OnboardingArrowDirection.Left => (-dy, dx),
                OnboardingArrowDirection.Up => (-dx, -dy),
                OnboardingArrowDirection.Right => (dy, -dx),
                _ => (dx, dy)
            };

            return new PointF(centerX + (rx * scale), centerY + (ry * scale));
        }

        PathF path = BuildArrowIconPath(Transform);

        // Same light color as the spotlight hole's own glow border (not a separate dark fill) — that's
        // what actually reads against the dark scrim; see DrawGlow. Breathes with the same shared
        // PulseIntensity signal (via PulseAlpha) so the arrow pulses in step with the glow, not on its
        // own separate clock.
        canvas.FillColor = GlowColor.WithAlpha(GlowColor.Alpha * ArrowOpacity * PulseAlpha);
        canvas.FillPath(path, WindingMode.NonZero);
    }

    /// <summary>0.65..1 fraction of full brightness for the current <see cref="PulseIntensity"/> — the
    /// glow (via its radius) and the arrow (via its fill alpha) both breathe off this same curve so they
    /// pulse in lockstep rather than independently.</summary>
    private float PulseAlpha => 0.65f + (0.35f * PulseIntensity);

    /// <summary>
    /// A single curved "swoosh" arrow (hooked tail + rounded arrowhead), transcribed from a supplied SVG's
    /// path data — every coordinate below is that SVG's own, run through <paramref name="transform"/>
    /// rather than embedding the SVG as a static asset. In its native, untransformed orientation the
    /// arrowhead points down (toward larger Y); see the direction table in <see cref="DrawArrow"/>.
    /// </summary>
    private static PathF BuildArrowIconPath(Func<float, float, PointF> transform)
    {
        var path = new PathF();
        PointF p;

        p = transform(468.705f, 258.685f); path.MoveTo(p.X, p.Y);
        p = transform(409.294f, 318.093f); path.LineTo(p.X, p.Y);
        AppendCurve(path, transform, 399.367f, 217.016f, 324.501f, 129.734f, 219.924f, 108.932f);
        AppendCurve(path, transform, 142.946f, 93.624f, 63.560f, 117.394f, 7.568f, 172.526f);
        AppendCurve(path, transform, -2.411f, 182.355f, -2.538f, 198.410f, 7.291f, 208.393f);
        AppendCurve(path, transform, 17.114f, 218.370f, 33.172f, 218.501f, 43.158f, 208.670f);
        AppendCurve(path, transform, 87.166f, 165.338f, 149.551f, 146.650f, 210.029f, 158.682f);
        AppendCurve(path, transform, 292.651f, 175.116f, 351.670f, 244.368f, 358.939f, 324.342f);
        p = transform(293.275f, 258.680f); path.LineTo(p.X, p.Y);
        AppendCurve(path, transform, 283.372f, 248.775f, 267.313f, 248.775f, 257.408f, 258.680f);
        AppendCurve(path, transform, 247.503f, 268.585f, 247.503f, 284.642f, 257.408f, 294.547f);
        p = transform(363.057f, 400.194f); path.LineTo(p.X, p.Y);
        AppendCurve(path, transform, 368.009f, 405.146f, 374.500f, 407.623f, 380.990f, 407.623f);
        AppendCurve(path, transform, 387.479f, 407.623f, 393.972f, 405.148f, 398.924f, 400.194f);
        p = transform(504.568f, 294.550f); path.LineTo(p.X, p.Y);
        AppendCurve(path, transform, 514.473f, 284.647f, 514.473f, 268.588f, 504.568f, 258.683f);
        AppendCurve(path, transform, 494.669f, 248.780f, 478.610f, 248.780f, 468.705f, 258.685f);
        path.Close();

        return path;
    }

    private static void AppendCurve(PathF path, Func<float, float, PointF> transform,
        float cp1X, float cp1Y, float cp2X, float cp2Y, float endX, float endY)
    {
        PointF cp1 = transform(cp1X, cp1Y);
        PointF cp2 = transform(cp2X, cp2Y);
        PointF end = transform(endX, endY);
        path.CurveTo(cp1.X, cp1.Y, cp2.X, cp2.Y, end.X, end.Y);
    }

    /// <summary>
    /// Traces the hole's exact edge (rect or circle) with several layered, semi-transparent strokes —
    /// widest and faintest on the outside, narrowing and brightening inward — to build a soft light
    /// falloff by hand. <see cref="ICanvas.SetShadow"/> from a fully transparent stroke was tried first
    /// and rendered nothing (this backend derives the shadow from the stroke's own painted alpha, so
    /// zero alpha means zero shadow too), so this doesn't depend on native shadow support at all.
    /// </summary>
    private void DrawGlow(ICanvas canvas, SpotlightGeometry geometry, RectF hole)
    {
        const int layers = 6;
        const float peakAlphaFraction = 0.6f; // cap even the innermost/brightest layer below GlowColor.Alpha

        // Pulse never fully disappears — breathes between 65% and 100% of GlowRadius/brightness, not
        // down to 0, so the glow stays visible even at the dim end of the cycle.
        float pulse = PulseAlpha;
        float radius = GlowRadius * pulse;

        var glowPath = new PathF();
        AppendHoleShape(glowPath, geometry, hole);

        canvas.SaveState();

        for (int i = 0; i < layers; i++)
        {
            // 0 at the outermost/widest/faintest layer, 1 at the innermost/thinnest/brightest.
            float progress = i / (float)(layers - 1);

            canvas.StrokeSize = Math.Max(1f, radius * (1f - progress));
            canvas.StrokeColor = GlowColor.WithAlpha(GlowColor.Alpha * progress * progress * peakAlphaFraction * pulse);
            canvas.DrawPath(glowPath);
        }

        canvas.RestoreState();
    }

    private static void AppendHoleShape(PathF path, SpotlightGeometry geometry, RectF hole)
    {
        if (geometry.Shape == OnboardingSpotlightShape.Circle)
            path.AppendEllipse(hole);
        else
            path.AppendRoundedRectangle(hole, (float)geometry.CornerRadius);
    }
}
