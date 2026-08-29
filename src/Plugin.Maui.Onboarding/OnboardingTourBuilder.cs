namespace Plugin.Maui.Onboarding;

/// <summary>Fluent builder for an <see cref="OnboardingTour"/>.</summary>
public sealed class OnboardingTourBuilder
{
    private readonly string _key;
    private readonly List<OnboardingStep> _steps = [];

    private OnboardingTourBuilder(string key) => _key = key;

    public static OnboardingTourBuilder Create(string tourKey) => new(tourKey);

    public OnboardingTourBuilder AddStep(OnboardingStep step)
    {
        _steps.Add(step);
        return this;
    }

    public OnboardingTour Build() => new(_key, _steps.AsReadOnly());
}
