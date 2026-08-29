using Plugin.Maui.Onboarding.Internals;

namespace Plugin.Maui.Onboarding.Views;

/// <summary>
/// Grid of the scrim/cutout <see cref="GraphicsView"/> plus a per-step <see cref="OnboardingTooltipCard"/>.
/// Owns the step-to-step animation: lerps the spotlight geometry via MAUI's <c>Animation</c>/<c>.Commit()</c>
/// idiom, then fades/scales the tooltip in via <c>Task.WhenAll(FadeTo, ScaleTo)</c>.
/// </summary>
public partial class OnboardingOverlayView : ContentView
{
    private const string GeometryAnimationName = "OnboardingSpotlightMove";
    private const string PulseAnimationName = "OnboardingGlowPulse";
    private const string ArrowFadeAnimationName = "OnboardingArrowFade";
    private const uint PulseCycleMs = 1400;
    private const double TooltipWidth = 280;

    // Arrow icon size: Thickness runs along the tooltip's edge, Length reaches back toward the spotlight
    // (the icon itself is contain-fit into this rect — see SpotlightDrawable.DrawArrow — so these just
    // set its available footprint). CornerRadius must match OnboardingTooltipCard.xaml's RoundRectangle
    // CornerRadius so the arrow never sits over a rounded corner.
    private const double ArrowThickness = 27;
    private const double ArrowLength = 19.5;
    private const double TooltipCornerRadius = 14;

    // How far each end of the arrow sits from the thing it's pointing at/from — the spotlight's edge on
    // the tip side, the tooltip's own edge on the base side — so the arrow visually floats between the
    // two rather than touching either.
    private const double ArrowGap = 6;

    // Overall tooltip<->spotlight spacing, sized so the arrow (ArrowLength) plus an ArrowGap on both ends
    // fits in the gap between them — replaces ChoosePlacement/ComputeTooltipOrigin's own 16px default,
    // which was sized for the old, smaller triangle and no longer leaves room for either gap.
    private const double TooltipMargin = ArrowLength + (2 * ArrowGap);

    private readonly SpotlightDrawable _drawable = new();
    private OnboardingTooltipCard? _tooltipCard;
    private CancellationTokenSource? _pulseCts;

    private Color? _scrimColor;
    private Color? _tooltipBackgroundColor;
    private Color? _tooltipBorderColor;

    public event EventHandler? NextRequested;
    public event EventHandler? SkipRequested;

    /// <summary>Explicit override for the scrim color, set via <see cref="OnboardingCoordinator.ScrimColor"/>.
    /// Null (the default) falls back to any <c>onboarding_scrim_light</c>/<c>onboarding_scrim_dark</c>
    /// app resource, and beneath that to <see cref="SpotlightDrawable"/>'s own compiled-in default —
    /// see <see cref="ApplyThemeScrimColor"/>. Non-null always wins over both.</summary>
    public Color? ScrimColor
    {
        get => _scrimColor;
        set { _scrimColor = value; ApplyThemeScrimColor(); }
    }

    /// <summary>Explicit override for the tooltip card's background, set via
    /// <see cref="OnboardingCoordinator.TooltipBackgroundColor"/>. Null (the default) leaves each new
    /// <see cref="OnboardingTooltipCard"/> at its own compiled-in default. Applied to every
    /// subsequently-created card (see <see cref="UpdateStepAsync"/>) and, if a card is already showing,
    /// to that one immediately.</summary>
    public Color? TooltipBackgroundColor
    {
        get => _tooltipBackgroundColor;
        set
        {
            _tooltipBackgroundColor = value;
            if (_tooltipCard is not null && value is not null)
                _tooltipCard.TooltipBackgroundColor = value;
        }
    }

    /// <summary>Explicit override for the tooltip card's border, set via
    /// <see cref="OnboardingCoordinator.TooltipBorderColor"/>. Null (the default) leaves each new
    /// <see cref="OnboardingTooltipCard"/> at its own compiled-in default (Transparent — no visible
    /// border). Applied to every subsequently-created card (see <see cref="UpdateStepAsync"/>) and, if a
    /// card is already showing, to that one immediately.</summary>
    public Color? TooltipBorderColor
    {
        get => _tooltipBorderColor;
        set
        {
            _tooltipBorderColor = value;
            if (_tooltipCard is not null && value is not null)
                _tooltipCard.TooltipBorderColor = value;
        }
    }

    public OnboardingOverlayView()
    {
        InitializeComponent();
        ScrimCanvas.Drawable = _drawable;
        _drawable.ArrowOpacity = 0;

        ApplyThemeScrimColor();
        if (Application.Current is not null)
            Application.Current.RequestedThemeChanged += (_, _) => ApplyThemeScrimColor();
    }

    /// <summary>Called by <see cref="OnboardingHostPage"/>'s hardware-back handling.</summary>
    public void RequestSkip() => SkipRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Animates from the currently-shown geometry (if any) to the new step's, then shows its text.
    /// <paramref name="content"/> is the step's <see cref="OnboardingStep.Content"/> factory (if any) —
    /// invoked here, once, so every step gets a fresh <see cref="View"/> instance same as the card itself
    /// (see the comment below), rather than one shared instance being reparented across steps/replays.</summary>
    public async Task UpdateStepAsync(SpotlightGeometry geometry, string title, string description, bool isLastStep, Func<View>? content)
    {
        StartPulse();

        SpotlightGeometry from = _drawable.Geometry ?? geometry;

        OnboardingTooltipCard? outgoing = _tooltipCard;
        if (outgoing is not null)
        {
            await Task.WhenAll(
                outgoing.FadeToAsync(0, 120, Easing.CubicIn),
                outgoing.ScaleToAsync(0.92, 120, Easing.CubicIn),
                AnimateArrowOpacityAsync(_drawable.ArrowOpacity, 0, 120));
            RootGrid.Children.Remove(outgoing);
        }

        // A fresh instance every step — see the comment in OnboardingOverlayView.xaml. Content is set
        // before adding it to the tree, so its first-ever layout pass already reflects it.
        var card = new OnboardingTooltipCard
        {
            WidthRequest = TooltipWidth,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Opacity = 0,
            Scale = 0.92,
            Title = title,
            Description = description,
            IsLastStep = isLastStep,
            StepContent = content?.Invoke(),
            NextCommand = new Command(() => NextRequested?.Invoke(this, EventArgs.Empty)),
            SkipCommand = new Command(() => SkipRequested?.Invoke(this, EventArgs.Empty))
        };
        if (TooltipBackgroundColor is { } tooltipBackgroundColor)
            card.TooltipBackgroundColor = tooltipBackgroundColor;
        if (TooltipBorderColor is { } tooltipBorderColor)
            card.TooltipBorderColor = tooltipBorderColor;

        _tooltipCard = card;
        RootGrid.Children.Add(card);

        await AnimateGeometryAsync(from, geometry);
        await PositionTooltipAsync(card, geometry.Bounds);

        await Task.WhenAll(
            card.FadeToAsync(1, 180, Easing.CubicOut),
            card.ScaleToAsync(1, 180, Easing.CubicOut),
            AnimateArrowOpacityAsync(0, 1, 180));
    }

    /// <summary>Resets for reuse the next time a tour starts (this view/page is reused across tours).</summary>
    public void Reset()
    {
        StopPulse();

        _drawable.Geometry = null;
        _drawable.Arrow = null;
        _drawable.ArrowOpacity = 0;
        ScrimCanvas.Invalidate();

        if (_tooltipCard is { } card)
        {
            RootGrid.Children.Remove(card);
            _tooltipCard = null;
        }
    }

    private void StartPulse()
    {
        if (_pulseCts is not null)
            return; // already running

        _pulseCts = new CancellationTokenSource();
        PulseLoop(_pulseCts.Token);
    }

    private void StopPulse()
    {
        _pulseCts?.Cancel();
        _pulseCts?.Dispose();
        _pulseCts = null;
        _drawable.PulseIntensity = 1f;
    }

    /// <summary>Loops the glow's breathing pulse indefinitely until cancelled, on its own clock rather
    /// than being driven by step changes.</summary>
    private async void PulseLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var tcs = new TaskCompletionSource();
            var anim = new Animation();
            anim.Add(0.00, 0.50, new Animation(v =>
            {
                _drawable.PulseIntensity = (float)v;
                ScrimCanvas.Invalidate();
            }, 0.35, 1, Easing.SinInOut));
            anim.Add(0.50, 1.00, new Animation(v =>
            {
                _drawable.PulseIntensity = (float)v;
                ScrimCanvas.Invalidate();
            }, 1, 0.35, Easing.SinInOut));
            anim.Commit(this, PulseAnimationName, length: PulseCycleMs, finished: (_, _) => tcs.TrySetResult());

            try { await tcs.Task.WaitAsync(token); }
            catch (OperationCanceledException) { return; }
        }
    }

    private Task AnimateGeometryAsync(SpotlightGeometry from, SpotlightGeometry to)
    {
        var tcs = new TaskCompletionSource();
        var animation = new Animation(v =>
        {
            _drawable.Geometry = OnboardingGeometryMath.Lerp(from, to, v);
            ScrimCanvas.Invalidate();
        }, 0, 1, Easing.CubicInOut);

        animation.Commit(this, GeometryAnimationName, 16, 280, finished: (_, _) => tcs.TrySetResult());
        return tcs.Task;
    }

    /// <summary>Mirrors <see cref="AnimateGeometryAsync"/>'s Animation/Commit/Invalidate idiom to fade the
    /// arrow's opacity in step with the tooltip card's own fade, since the arrow is painted on
    /// ScrimCanvas rather than being its own VisualElement.</summary>
    private Task AnimateArrowOpacityAsync(float from, float to, uint length)
    {
        var tcs = new TaskCompletionSource();
        var animation = new Animation(v =>
        {
            _drawable.ArrowOpacity = (float)v;
            ScrimCanvas.Invalidate();
        }, from, to, Easing.Linear);

        animation.Commit(this, ArrowFadeAnimationName, 16, length, finished: (_, _) => tcs.TrySetResult());
        return tcs.Task;
    }

    private async Task PositionTooltipAsync(OnboardingTooltipCard card, Rect spotlight)
    {
        Size screen = await GetLaidOutSizeAsync();
        if (screen.Width <= 0 || screen.Height <= 0)
            return;

        double height = await WaitForFirstLayoutHeightAsync(card);
        var tooltipSize = new Size(TooltipWidth, height);
        OnboardingTooltipPlacement placement = OnboardingGeometryMath.ChoosePlacement(spotlight, screen, tooltipSize, TooltipMargin);
        Point origin = OnboardingGeometryMath.ComputeTooltipOrigin(spotlight, screen, tooltipSize, placement, TooltipMargin);

        card.Margin = new Thickness(origin.X, origin.Y, 0, 0);

        var tooltipBounds = new Rect(origin.X, origin.Y, tooltipSize.Width, tooltipSize.Height);
        Rect arrowBounds = OnboardingGeometryMath.ComputeArrowBounds(spotlight, tooltipBounds, placement, ArrowThickness, ArrowLength, TooltipCornerRadius, ArrowGap);
        _drawable.Arrow = (arrowBounds, OnboardingGeometryMath.GetArrowDirection(placement));
        ScrimCanvas.Invalidate();
    }

    /// <summary>Waits (briefly) for the first layout pass so Width/Height are populated on the very first step.</summary>
    private async Task<Size> GetLaidOutSizeAsync()
    {
        for (int i = 0; i < 20 && (Width <= 0 || Height <= 0); i++)
            await Task.Delay(25);

        return new Size(Width, Height);
    }

    /// <summary>Waits for a freshly-added card's first real layout pass to populate its Height, falling back to Measure() if it never does.</summary>
    private static async Task<double> WaitForFirstLayoutHeightAsync(OnboardingTooltipCard card)
    {
        for (int i = 0; i < 20 && card.Height <= 0; i++)
            await Task.Delay(16);

        return card.Height > 0 ? card.Height : card.Measure(TooltipWidth, double.PositiveInfinity).Height;
    }

    /// <summary>
    /// Resolves the scrim color in priority order: an explicit <see cref="ScrimColor"/> set via the public
    /// API always wins; failing that, optional host-app overrides — <c>onboarding_scrim_light</c>/
    /// <c>onboarding_scrim_dark</c> <see cref="Color"/> resources — are looked up and applied whichever
    /// matches the current theme; left undefined too, this quietly no-ops and
    /// <see cref="SpotlightDrawable"/>'s own compiled-in default color stands.
    /// </summary>
    private void ApplyThemeScrimColor()
    {
        if (ScrimColor is { } explicitColor)
        {
            _drawable.ScrimColor = explicitColor;
            ScrimCanvas.Invalidate();
            return;
        }

        string key = Application.Current?.RequestedTheme == AppTheme.Dark ? "onboarding_scrim_dark" : "onboarding_scrim_light";
        if (Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is Color color)
        {
            _drawable.ScrimColor = color;
            ScrimCanvas.Invalidate();
        }
    }
}
