namespace Plugin.Maui.Onboarding;

/// <summary>An ordered, named sequence of onboarding steps. Build one via <see cref="OnboardingTourBuilder"/>.</summary>
public sealed record OnboardingTour(string Key, IReadOnlyList<OnboardingStep> Steps);
