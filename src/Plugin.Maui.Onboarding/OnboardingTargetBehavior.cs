namespace Plugin.Maui.Onboarding;

/// <summary>
/// Attach to any <see cref="VisualElement"/> to make it a named onboarding target, e.g.:
/// <code>
/// &lt;Button&gt;
///     &lt;Button.Behaviors&gt;
///         &lt;onboarding:OnboardingTargetBehavior Key="SignButton" /&gt;
///     &lt;/Button.Behaviors&gt;
/// &lt;/Button&gt;
/// </code>
/// Registers/re-registers into <see cref="Internals.OnboardingTargetRegistry"/> on attach (pages can be
/// recreated on Shell tab re-entry, so re-registering every attach is correct) and removes itself on detach.
/// </summary>
public class OnboardingTargetBehavior : Behavior<VisualElement>
{
    public static readonly BindableProperty KeyProperty =
        BindableProperty.Create(nameof(Key), typeof(string), typeof(OnboardingTargetBehavior));

    public string? Key
    {
        get => (string?)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);

        if (!string.IsNullOrEmpty(Key))
            Internals.OnboardingTargetRegistry.Register(Key, bindable);
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        base.OnDetachingFrom(bindable);

        if (!string.IsNullOrEmpty(Key))
            Internals.OnboardingTargetRegistry.Unregister(Key, bindable);
    }
}
