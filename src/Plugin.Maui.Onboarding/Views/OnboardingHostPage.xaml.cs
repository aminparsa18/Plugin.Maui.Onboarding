namespace Plugin.Maui.Onboarding.Views;

/// <summary>
/// The single reusable transparent modal page hosting <see cref="OnboardingOverlayView"/>. Owned and
/// push/popped by <see cref="Internals.OnboardingOverlayHost"/> — never constructed anywhere else.
/// </summary>
public partial class OnboardingHostPage : ContentPage
{
    /// <summary>Public handle onto the x:Name-generated (private-by-default) OverlayView field, for <see cref="Internals.OnboardingOverlayHost"/>.</summary>
    public OnboardingOverlayView Overlay { get; }

    public OnboardingHostPage()
    {
        InitializeComponent();
        BackgroundColor = Colors.Transparent;
        Overlay = OverlayView;
    }

    /// <summary>Android hardware back acts as Skip instead of silently dismissing the modal out from under the coordinator's state.</summary>
    protected override bool OnBackButtonPressed()
    {
        Overlay.RequestSkip();
        return true;
    }
}
