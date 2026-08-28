using Plugin.Maui.Onboarding.Views;

namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// Owns the single <see cref="OnboardingHostPage"/> for the app's lifetime — lazily created on the first
/// tour, pushed once per tour via <c>Shell.Current.Navigation.PushModalAsync</c> (not Mopups — see
/// <see cref="OnboardingCoordinator"/>), popped when the tour ends, and reused as-is by every later tour.
/// </summary>
public sealed class OnboardingOverlayHost
{
    private OnboardingHostPage? _page;
    private bool _isPushed;
    private Color? _scrimColor;
    private Color? _tooltipBackgroundColor;
    private Color? _tooltipBorderColor;

    public event EventHandler? NextRequested;
    public event EventHandler? SkipRequested;

    /// <summary>Forwarded to <see cref="OnboardingOverlayView.ScrimColor"/> — stored here too so it still
    /// applies once the (lazily-created) page/overlay exists, however early the caller sets it.</summary>
    public Color? ScrimColor
    {
        get => _scrimColor;
        set
        {
            _scrimColor = value;
            if (_page is not null)
                _page.Overlay.ScrimColor = value;
        }
    }

    /// <summary>Forwarded to <see cref="OnboardingOverlayView.TooltipBackgroundColor"/> — same lazy-page
    /// caveat as <see cref="ScrimColor"/>.</summary>
    public Color? TooltipBackgroundColor
    {
        get => _tooltipBackgroundColor;
        set
        {
            _tooltipBackgroundColor = value;
            if (_page is not null)
                _page.Overlay.TooltipBackgroundColor = value;
        }
    }

    /// <summary>Forwarded to <see cref="OnboardingOverlayView.TooltipBorderColor"/> — same lazy-page
    /// caveat as <see cref="ScrimColor"/>.</summary>
    public Color? TooltipBorderColor
    {
        get => _tooltipBorderColor;
        set
        {
            _tooltipBorderColor = value;
            if (_page is not null)
                _page.Overlay.TooltipBorderColor = value;
        }
    }

    public async Task ShowAsync()
    {
        if (_page is null)
        {
            _page = new OnboardingHostPage();
            _page.Overlay.ScrimColor = _scrimColor;
            _page.Overlay.TooltipBackgroundColor = _tooltipBackgroundColor;
            _page.Overlay.TooltipBorderColor = _tooltipBorderColor;
            _page.Overlay.NextRequested += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);
            _page.Overlay.SkipRequested += (_, _) => SkipRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _page.Overlay.Reset();
        }

        if (!_isPushed && Shell.Current is not null)
        {
            await Shell.Current.Navigation.PushModalAsync(_page, animated: false);
            _isPushed = true;
        }
    }

    public async Task HideAsync()
    {
        if (_isPushed && Shell.Current is not null)
        {
            await Shell.Current.Navigation.PopModalAsync(animated: false);
            _isPushed = false;
        }
    }

    public Task UpdateStepAsync(SpotlightGeometry geometry, string title, string description, bool isLastStep)
        => _page?.Overlay.UpdateStepAsync(geometry, title, description, isLastStep) ?? Task.CompletedTask;
}
