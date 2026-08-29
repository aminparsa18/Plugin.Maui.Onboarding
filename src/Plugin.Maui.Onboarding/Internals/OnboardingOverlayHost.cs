using Plugin.Maui.Onboarding.Views;

namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// Owns the single <see cref="OnboardingHostPage"/> for the app's lifetime — lazily created on the first
/// tour, pushed once per tour via <c>Shell.Current.Navigation.PushModalAsync</c> (not Mopups — see
/// <see cref="OnboardingCoordinator"/>), popped when the tour ends, and reused as-is by every later tour.
/// </summary>
public sealed class OnboardingOverlayHost
{
    // Guards _page/_isPushed across ShowAsync/HideAsync so two overlapping calls (e.g. a double-tapped
    // "replay tour" action) can't both observe _isPushed == false and both push the page. OnboardingCoordinator
    // happens to serialize the simplest case (its own state is set before its first await), but that's
    // incidental, not a guarantee this class should rely on - it owns this mutable lifecycle state, so it
    // protects it itself. Never disposed: this host is a field on OnboardingCoordinator and lives for the
    // app's lifetime, same as everything else here.
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

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

    /// <exception cref="InvalidOperationException"><see cref="Shell.Current"/> is null. This library
    /// requires a Shell-based host app (see the class doc) - failing loudly here beats silently returning
    /// with the overlay never actually shown.</exception>
    public async Task ShowAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
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

            if (!_isPushed)
            {
                Shell shell = Shell.Current ?? throw new InvalidOperationException(
                    "Plugin.Maui.Onboarding requires an active Shell (Shell.Current was null).");

                await shell.Navigation.PushModalAsync(_page, animated: false);
                _isPushed = true;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <exception cref="InvalidOperationException"><see cref="Shell.Current"/> is null while a tour's
    /// overlay is still pushed - same rationale as <see cref="ShowAsync"/>.</exception>
    public async Task HideAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (!_isPushed)
                return;

            Shell shell = Shell.Current ?? throw new InvalidOperationException(
                "Plugin.Maui.Onboarding requires an active Shell (Shell.Current was null) to dismiss the " +
                "onboarding overlay.");

            await shell.Navigation.PopModalAsync(animated: false);
            _isPushed = false;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task UpdateStepAsync(SpotlightGeometry geometry, string title, string description, bool isLastStep, Func<View>? content)
        => _page?.Overlay.UpdateStepAsync(geometry, title, description, isLastStep, content) ?? Task.CompletedTask;
}
