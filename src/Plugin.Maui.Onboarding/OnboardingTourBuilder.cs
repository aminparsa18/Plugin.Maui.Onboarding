namespace Plugin.Maui.Onboarding;

/// <summary>Fluent builder for an <see cref="OnboardingTour"/> — stamps <see cref="OnboardingStep.IsLastStep"/> on <see cref="Build"/>.</summary>
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

    public OnboardingTour Build()
    {
        if (_steps.Count > 0)
            _steps[^1] = _steps[^1] with { IsLastStep = true };

        return new OnboardingTour(_key, _steps.AsReadOnly());
    }
}
