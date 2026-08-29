namespace Plugin.Maui.Onboarding;

/// <summary>
/// One stop of an <see cref="OnboardingTour"/> — a control to spotlight plus the text explaining it.
/// Built via <see cref="OnboardingTourBuilder"/>.
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

    /// <summary>
    /// Optional factory for extra content (a tutorial GIF, a custom <see cref="ContentView"/>, anything)
    /// shown on the tooltip card between the description and the Next/Skip buttons. A factory rather than
    /// a pre-built <see cref="View"/> or a <see cref="Type"/>: the tooltip card is rebuilt from scratch on
    /// every step (see <c>OnboardingOverlayView.UpdateStepAsync</c>), so a single shared instance can't be
    /// reparented safely across replays, and a <see cref="Type"/> would need reflection to instantiate —
    /// fragile under iOS's Native AOT trimming when nothing else in the app directly <c>new</c>s that type.
    /// A closure also lets a step pass itself data (e.g. which GIF) without any side channel. Invoked once
    /// per display, on the UI thread.
    /// </summary>
    public Func<View>? Content { get; init; }
}
