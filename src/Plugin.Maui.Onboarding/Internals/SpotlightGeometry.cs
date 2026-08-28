namespace Plugin.Maui.Onboarding.Internals;

/// <summary>The resolved, on-screen shape <see cref="SpotlightDrawable"/> cuts out of the scrim for the current step.</summary>
public sealed record SpotlightGeometry(Rect Bounds, OnboardingSpotlightShape Shape, double CornerRadius);
