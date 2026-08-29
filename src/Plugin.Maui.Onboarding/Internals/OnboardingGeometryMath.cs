namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// Pure geometry helpers for the spotlight overlay: interpolating between two steps' geometry for the
/// step-to-step animation, and auto-placing the tooltip card beside the spotlight without clipping it
/// or covering the target. No MAUI view/handler dependency — plain <see cref="Rect"/>/<see cref="Size"/> math.
/// </summary>
public static class OnboardingGeometryMath
{
    /// <summary>Interpolates bounds/corner radius linearly; the shape itself doesn't morph (e.g. rectangle
    /// into circle) - it holds <paramref name="from"/>'s shape for the whole transition and only switches
    /// to <paramref name="to"/>'s shape on the final frame (t = 1), the same frame the bounds finish
    /// arriving. Switching mid-transition instead (e.g. at t = 0.5) would pop the hole's outline from one
    /// shape to the other while it's still visibly sliding/resizing, which is far more noticeable than a
    /// switch that lands in the same instant the move animation settles.</summary>
    public static SpotlightGeometry Lerp(SpotlightGeometry from, SpotlightGeometry to, double t)
    {
        t = Math.Clamp(t, 0, 1);

        var bounds = new Rect(
            from.Bounds.X + ((to.Bounds.X - from.Bounds.X) * t),
            from.Bounds.Y + ((to.Bounds.Y - from.Bounds.Y) * t),
            from.Bounds.Width + ((to.Bounds.Width - from.Bounds.Width) * t),
            from.Bounds.Height + ((to.Bounds.Height - from.Bounds.Height) * t));

        double cornerRadius = from.CornerRadius + ((to.CornerRadius - from.CornerRadius) * t);
        OnboardingSpotlightShape shape = t < 1 ? from.Shape : to.Shape;

        return new SpotlightGeometry(bounds, shape, cornerRadius);
    }

    /// <summary>
    /// Picks which side of the spotlight has room for the tooltip. Prefers below/above (the classic
    /// coach-mark look) over left/right, falling back to whichever side actually fits the tooltip's
    /// measured size, and finally to the wider of left/right if neither vertical side fits.
    /// </summary>
    public static OnboardingTooltipPlacement ChoosePlacement(Rect spotlight, Size screen, Size tooltip, double margin = 16)
    {
        double spaceAbove = spotlight.Top - margin;
        double spaceBelow = screen.Height - spotlight.Bottom - margin;
        double spaceLeft = spotlight.Left - margin;
        double spaceRight = screen.Width - spotlight.Right - margin;

        bool fitsBelow = spaceBelow >= tooltip.Height;
        bool fitsAbove = spaceAbove >= tooltip.Height;
        bool fitsRight = spaceRight >= tooltip.Width;
        bool fitsLeft = spaceLeft >= tooltip.Width;

        if (fitsBelow && (!fitsAbove || spaceBelow >= spaceAbove))
            return OnboardingTooltipPlacement.Below;

        if (fitsAbove)
            return OnboardingTooltipPlacement.Above;

        if (fitsRight && (!fitsLeft || spaceRight >= spaceLeft))
            return OnboardingTooltipPlacement.Right;

        if (fitsLeft)
            return OnboardingTooltipPlacement.Left;

        // Nothing actually fits (spotlight too large relative to the screen) - fall back to whichever
        // side has the most room; ComputeTooltipOrigin still clamps the result to stay on-screen.
        return spaceRight >= spaceLeft ? OnboardingTooltipPlacement.Right : OnboardingTooltipPlacement.Left;
    }

    /// <summary>Computes the tooltip's top-left origin for the given placement, clamped so it never overflows the screen edges.</summary>
    public static Point ComputeTooltipOrigin(Rect spotlight, Size screen, Size tooltip, OnboardingTooltipPlacement placement, double margin = 16)
    {
        double x;
        double y;

        switch (placement)
        {
            case OnboardingTooltipPlacement.Above:
                x = spotlight.Center.X - (tooltip.Width / 2);
                y = spotlight.Top - margin - tooltip.Height;
                break;
            case OnboardingTooltipPlacement.Below:
                x = spotlight.Center.X - (tooltip.Width / 2);
                y = spotlight.Bottom + margin;
                break;
            case OnboardingTooltipPlacement.Left:
                x = spotlight.Left - margin - tooltip.Width;
                y = spotlight.Center.Y - (tooltip.Height / 2);
                break;
            case OnboardingTooltipPlacement.Right:
            default:
                x = spotlight.Right + margin;
                y = spotlight.Center.Y - (tooltip.Height / 2);
                break;
        }

        x = Math.Clamp(x, margin, Math.Max(margin, screen.Width - tooltip.Width - margin));
        y = Math.Clamp(y, margin, Math.Max(margin, screen.Height - tooltip.Height - margin));

        return new Point(x, y);
    }

    /// <summary>The arrow always points back from the tooltip toward the spotlight — the opposite side
    /// from wherever <see cref="ChoosePlacement"/> put the tooltip.</summary>
    public static OnboardingArrowDirection GetArrowDirection(OnboardingTooltipPlacement placement) => placement switch
    {
        OnboardingTooltipPlacement.Above => OnboardingArrowDirection.Down,
        OnboardingTooltipPlacement.Below => OnboardingArrowDirection.Up,
        OnboardingTooltipPlacement.Left => OnboardingArrowDirection.Right,
        OnboardingTooltipPlacement.Right => OnboardingArrowDirection.Left,
        _ => OnboardingArrowDirection.Up
    };

    /// <summary>
    /// Bounds (in the same screen coordinate space as <paramref name="tooltip"/>) for the small triangle
    /// that bridges the tooltip's edge to the spotlight. Sits in the gap <see cref="ComputeTooltipOrigin"/>
    /// placed the tooltip across, centered on the spotlight along the edge it sits on but clamped inside
    /// <paramref name="cornerRadius"/> of the tooltip's corners so it never rides up onto the rounded
    /// corner. <paramref name="thickness"/> is measured along the tooltip's edge, <paramref name="length"/>
    /// perpendicular to it (how far it reaches back toward the spotlight); <paramref name="tailGap"/> is
    /// the clearance left between the arrow's base and the tooltip's own edge (not touching it) — callers
    /// sizing the tooltip's own placement margin (see OnboardingOverlayView's TooltipMargin) need to budget
    /// for this same value so there's actually room for it.
    /// </summary>
    public static Rect ComputeArrowBounds(Rect spotlight, Rect tooltip, OnboardingTooltipPlacement placement, double thickness, double length, double cornerRadius, double tailGap)
    {
        switch (placement)
        {
            case OnboardingTooltipPlacement.Below: // tooltip under the target -> arrow on its top edge, pointing up
                return new Rect(ClampAcross(spotlight.Center.X, tooltip.Left, tooltip.Right, cornerRadius, thickness), tooltip.Top - length - tailGap, thickness, length);

            case OnboardingTooltipPlacement.Above: // tooltip over the target -> arrow on its bottom edge, pointing down
                return new Rect(ClampAcross(spotlight.Center.X, tooltip.Left, tooltip.Right, cornerRadius, thickness), tooltip.Bottom + tailGap, thickness, length);

            case OnboardingTooltipPlacement.Right: // tooltip right of the target -> arrow on its left edge, pointing left
                return new Rect(tooltip.Left - length - tailGap, ClampAcross(spotlight.Center.Y, tooltip.Top, tooltip.Bottom, cornerRadius, thickness), length, thickness);

            case OnboardingTooltipPlacement.Left: // tooltip left of the target -> arrow on its right edge, pointing right
            default:
                return new Rect(tooltip.Right + tailGap, ClampAcross(spotlight.Center.Y, tooltip.Top, tooltip.Bottom, cornerRadius, thickness), length, thickness);
        }
    }

    /// <summary>Clamps a centered <paramref name="thickness"/>-wide span around <paramref name="center"/>
    /// to stay within [<paramref name="edgeStart"/> + cornerRadius, <paramref name="edgeEnd"/> -
    /// cornerRadius], falling back to dead-center of the edge if the edge is too short to fit both insets.</summary>
    private static double ClampAcross(double center, double edgeStart, double edgeEnd, double cornerRadius, double thickness)
    {
        double min = edgeStart + cornerRadius;
        double max = edgeEnd - cornerRadius - thickness;
        double target = center - (thickness / 2);

        return max >= min ? Math.Clamp(target, min, max) : edgeStart + ((edgeEnd - edgeStart - thickness) / 2);
    }
}
