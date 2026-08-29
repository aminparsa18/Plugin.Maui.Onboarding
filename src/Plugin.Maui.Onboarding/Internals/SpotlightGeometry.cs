namespace Plugin.Maui.Onboarding.Internals;

/// <summary>The resolved, on-screen shape <see cref="SpotlightDrawable"/> cuts out of the scrim for the current step.</summary>
public sealed record SpotlightGeometry(Rect Bounds, OnboardingSpotlightShape Shape, double CornerRadius)
{
    /// <summary>Clamped to [0, min(Bounds.Width, Bounds.Height) / 2] so it's always a value
    /// <see cref="SpotlightDrawable"/> can hand straight to <c>PathF.AppendRoundedRectangle</c> without
    /// producing a self-intersecting path. <see cref="OnboardingStep.CornerRadius"/> is a consumer-supplied,
    /// unvalidated value, and <see cref="OnboardingGeometryMath.Lerp"/> interpolates bounds and corner
    /// radius independently - a radius that's valid for both of two steps' own bounds isn't guaranteed
    /// valid against their interpolated mid-animation bounds. Enforced here, the one place every
    /// <see cref="SpotlightGeometry"/> is constructed, rather than by each caller.</summary>
    public double CornerRadius { get; init; } = ClampCornerRadius(Bounds, CornerRadius);

    private static double ClampCornerRadius(Rect bounds, double cornerRadius)
    {
        double maxRadius = Math.Max(0, Math.Min(bounds.Width, bounds.Height) / 2);
        return Math.Clamp(cornerRadius, 0, maxRadius);
    }
}
