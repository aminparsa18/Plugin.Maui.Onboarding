namespace Plugin.Maui.Onboarding;

/// <summary>
/// One stop of an <see cref="OnboardingTour"/> — a control to spotlight plus the text explaining it.
/// Built via <see cref="OnboardingTourBuilder"/>, which stamps <see cref="IsLastStep"/>.
/// </summary>
public sealed record OnboardingStep
{
    /// <summary>Matches the <c>Key</c> an <see cref="OnboardingTargetBehavior"/> registered elsewhere in the UI.</summary>
    public required string TargetKey { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public OnboardingSpotlightShape Shape { get; init; } = OnboardingSpotlightShape.RoundedRectangle;

    public double CornerRadius { get; init; } = 12;

    /// <summary>Extra room left around the target's own bounds when cutting the spotlight hole.</summary>
    public double SpotlightPadding { get; init; } = 8;

    /// <summary>
    /// Shell route to navigate to (e.g. <c>"//TimesheetsMonthView"</c>) before resolving this step's
    /// target — null means the target is expected to already be on screen from the previous step.
    /// </summary>
    public string? RequiredRoute { get; init; }

    /// <summary>Stamped by <see cref="OnboardingTourBuilder.Build"/> — true for the tour's final step.</summary>
    public bool IsLastStep { get; internal set; }
}
