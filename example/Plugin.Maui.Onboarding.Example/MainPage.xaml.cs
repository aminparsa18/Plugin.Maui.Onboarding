namespace Plugin.Maui.Onboarding.Example;

public partial class MainPage : ContentPage
{
    // One coordinator instance drives every tour started from this page. It's cheap to keep around —
    // StartTourIfNotCompletedAsync/ReplayTourAsync are both no-ops (or safely re-entrant) if a tour is
    // already active, so re-running OnAppearing on tab re-entry doesn't double-start anything.
    private readonly OnboardingCoordinator _onboarding = new();

    public MainPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // First launch only — StartTourIfNotCompletedAsync no-ops once "MainPageTour" has been
        // completed or skipped. Use the "Replay onboarding tour" button below to see it again.
        await _onboarding.StartTourIfNotCompletedAsync(BuildTour());
    }

    private async void OnReplayTourClicked(object? sender, EventArgs e)
        => await _onboarding.ReplayTourAsync(BuildTour());

    /// <summary>
    /// Walks the four controls tagged with <see cref="OnboardingTargetBehavior"/> in MainPage.xaml, in
    /// on-screen order. <see cref="OnboardingTourBuilder"/> stamps <c>IsLastStep</c> on the final step.
    /// </summary>
    private static OnboardingTour BuildTour() =>
        OnboardingTourBuilder.Create("MainPageTour")
            .AddStep(new OnboardingStep
            {
                TargetKey = "SearchBar",
                Title = "Search your notes",
                Description = "Type here to quickly find any note by its title.",
                Shape = OnboardingSpotlightShape.RoundedRectangle
            })
            .AddStep(new OnboardingStep
            {
                TargetKey = "FilterButton",
                Title = "Filter & sort",
                Description = "Tap here to change how your notes are filtered and sorted.",
                Shape = OnboardingSpotlightShape.Circle
            })
            .AddStep(new OnboardingStep
            {
                TargetKey = "ItemsList",
                Title = "Your notes",
                Description = "Everything you save shows up here, newest first.",
                Shape = OnboardingSpotlightShape.RoundedRectangle
            })
            .AddStep(new OnboardingStep
            {
                TargetKey = "AddButton",
                Title = "Add a note",
                Description = "Tap the plus button any time to create a new note.",
                Shape = OnboardingSpotlightShape.Circle
            })
            .Build();
}
