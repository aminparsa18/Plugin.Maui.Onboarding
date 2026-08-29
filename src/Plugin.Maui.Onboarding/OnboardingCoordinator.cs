using Plugin.Maui.Onboarding.Internals;

namespace Plugin.Maui.Onboarding;

/// <summary>
/// Public entry point for onboarding tours — the one type consumers use. Everything else in this
/// library is an implementation detail it owns directly; skipping unit tests for now removes the main
/// reason to abstract the internal collaborators behind interfaces.
/// </summary>
public sealed class OnboardingCoordinator
{
    // A CollectionView's native RecyclerView has been observed taking several seconds to settle to a
    // stable measured size while its content is still loading (repeated transient zero-size reads
    // during in-flight relayout passes) — 5s left the locator resolving right at the wire. 10s gives
    // real headroom without meaningfully changing the "give up and skip" experience if a target is
    // genuinely never going to appear.
    private static readonly TimeSpan TargetResolveTimeout = TimeSpan.FromSeconds(10);

    // Serializes StartTourIfNotCompletedAsync/ReplayTourAsync/NextAsync/SkipAsync - the only entry points
    // that mutate _tour/_stepIndex/_resolvedGeometries - so one call's state changes can't interleave with
    // another's. Without this, e.g. two overlapping ReplayTourAsync calls can have the second one's
    // CompleteAsync() null out _resolvedGeometries while the first is still mid-loop writing into it by
    // index, throwing IndexOutOfRangeException. Only acquired here, at the public surface: BeginAsync/
    // AdvanceAsync/CompleteAsync are private and only ever called from a method that already holds it, so
    // they never re-acquire it themselves (SemaphoreSlim isn't reentrant - acquiring it twice on the same
    // logical call chain would deadlock).
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly OnboardingOverlayHost _host = new();
    private readonly OnboardingTargetLocator _locator = new();
    private bool _isSubscribed;

    private OnboardingTour? _tour;
    private int _stepIndex = -1;

    // Resolved for every step BEFORE the overlay modal is pushed — see BeginAsync. null at an index
    // means that step's target never appeared and AdvanceAsync skips it.
    private SpotlightGeometry?[] _resolvedGeometries = [];

    public bool IsTourActive => _tour is not null;

    /// <summary>Overrides the scrim's default color (`#B3000000`, or an <c>onboarding_scrim_light</c>/
    /// <c>onboarding_scrim_dark</c> app resource if defined). Null (the default) leaves those defaults in
    /// place. Can be set at any time, including mid-tour.</summary>
    public Color? ScrimColor
    {
        get => _host.ScrimColor;
        set => _host.ScrimColor = value;
    }

    /// <summary>Overrides the tooltip card's default background color (`#E6000000`). Null (the default)
    /// leaves that default in place. Can be set at any time, including mid-tour.</summary>
    public Color? TooltipBackgroundColor
    {
        get => _host.TooltipBackgroundColor;
        set => _host.TooltipBackgroundColor = value;
    }

    /// <summary>Overrides the tooltip card's default border color (Transparent — no visible border).
    /// Null (the default) leaves that default in place. Can be set at any time, including mid-tour.</summary>
    public Color? TooltipBorderColor
    {
        get => _host.TooltipBorderColor;
        set => _host.TooltipBorderColor = value;
    }

    /// <summary>No-ops if a tour is already active or this one's already been completed.</summary>
    public async Task StartTourIfNotCompletedAsync(OnboardingTour tour, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (IsTourActive || tour.Steps.Count == 0 || OnboardingCompletionStore.IsCompleted(tour.Key))
                return;

            await BeginAsync(tour, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Resets the tour's completion flag and restarts it, skipping any tour currently in progress first.</summary>
    public async Task ReplayTourAsync(OnboardingTour tour, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (IsTourActive)
                await CompleteAsync();

            OnboardingCompletionStore.SetCompleted(tour.Key, false);
            await BeginAsync(tour, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task NextAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await AdvanceAsync(CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Also marks the tour completed — matches "Done" semantics, so it won't nag again.</summary>
    public async Task SkipAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (IsTourActive)
                await CompleteAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task BeginAsync(OnboardingTour tour, CancellationToken ct)
    {
        if (tour.Steps.Count == 0)
            return;

        _tour = tour;
        _stepIndex = -1;

        // Resolve every step's target bounds BEFORE the overlay modal is pushed. Once the modal covers
        // the page, native bounds lookups against some controls behind it (observed with a CollectionView
        // — its RecyclerView) can stall indefinitely rather than merely being delayed, regardless of how
        // long the locator is willing to wait — so waiting it out post-push isn't a fix. Resolving
        // everything up front, while the page is still fully on-screen and uncovered (completely
        // ordinary layout conditions, nothing exotic), sidesteps that entirely. Steps with a
        // RequiredRoute still navigate first — for a single-page tour like today's this all happens
        // before anything is shown, so there's no visible flicker; a future cross-page tour would show
        // its own brief tab-switching here before the overlay ever appears, which is an acceptable and
        // separate concern from today's bug.
        _resolvedGeometries = new SpotlightGeometry?[tour.Steps.Count];
        for (int i = 0; i < tour.Steps.Count; i++)
        {
            OnboardingStep step = tour.Steps[i];

            if (step.RequiredRoute is not null && Shell.Current is not null)
                await Shell.Current.GoToAsync(step.RequiredRoute);

            Rect? bounds = await _locator.ResolveBoundsAsync(step.TargetKey, TargetResolveTimeout, ct);
            _resolvedGeometries[i] = bounds is { } b
                ? new SpotlightGeometry(Inflate(b, step.SpotlightPadding), step.Shape, step.CornerRadius)
                : null; // target never appeared — AdvanceAsync skips this index
        }

        if (!_isSubscribed)
        {
            _host.NextRequested += OnNextRequested;
            _host.SkipRequested += OnSkipRequested;
            _isSubscribed = true;
        }

        await _host.ShowAsync();
        await AdvanceAsync(ct);
    }

    private async Task AdvanceAsync(CancellationToken ct)
    {
        if (_tour is null)
            return;

        _stepIndex++;
        if (_stepIndex >= _tour.Steps.Count)
        {
            await CompleteAsync();
            return;
        }

        SpotlightGeometry? geometry = _resolvedGeometries[_stepIndex];
        if (geometry is null)
        {
            // Target never appeared during pre-resolution — skip rather than show a bogus/empty hole.
            await AdvanceAsync(ct);
            return;
        }

        OnboardingStep step = _tour.Steps[_stepIndex];
        await _host.UpdateStepAsync(geometry, step.Title, step.Description, step.IsLastStep);
    }

    private async Task CompleteAsync()
    {
        if (_tour is null)
            return;

        OnboardingCompletionStore.SetCompleted(_tour.Key, true);
        await _host.HideAsync();
        _tour = null;
        _stepIndex = -1;
        _resolvedGeometries = [];
    }

    // Routed through the public NextAsync/SkipAsync (not AdvanceAsync/CompleteAsync directly) so a tooltip
    // tap is serialized by _gate the same as any other entry point - see _gate's doc comment.
    private void OnNextRequested(object? sender, EventArgs e) => _ = RunAndLogAsync(NextAsync);

    private void OnSkipRequested(object? sender, EventArgs e) => _ = RunAndLogAsync(SkipAsync);

    /// <summary>
    /// Fire-and-forget event handlers (Next/Skip taps) discard the returned Task, so an exception
    /// anywhere in the awaited chain would otherwise vanish silently — the tour just appears to
    /// "stop updating" partway through a step with no visible error. Route through here instead so
    /// it at least surfaces in logcat/Debug output.
    /// </summary>
    private static async Task RunAndLogAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Onboarding] unhandled exception in fire-and-forget handler: {ex}");
        }
    }

    private static Rect Inflate(Rect bounds, double padding)
        => new(bounds.X - padding, bounds.Y - padding, bounds.Width + (padding * 2), bounds.Height + (padding * 2));
}
