using Plugin.Maui.Onboarding.Internals;

namespace Plugin.Maui.Onboarding;

/// <summary>
/// Public entry point for onboarding tours — the one type consumers use. Everything else in this
/// library is an implementation detail it owns directly; skipping unit tests for now removes the main
/// reason to abstract the internal collaborators behind interfaces.
/// </summary>
/// <remarks>
/// Not <see cref="IDisposable"/>, and not meant to be freely instantiated and discarded — hold one
/// instance for as long as its host page (or a longer-lived scope, e.g. a DI singleton) is alive, the
/// same as the example app does. The one thing to actually avoid is discarding an instance while
/// <see cref="IsTourActive"/> is true: the overlay it pushed onto <c>Shell.Current.Navigation</c> has no
/// other way to get popped once the coordinator that owns it is gone. Short of that, an unused instance
/// (a tour never started, or one that already finished/was skipped) costs nothing beyond ordinary GC
/// reachability — it holds no unmanaged resources.
/// </remarks>
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
    // EndTourAsync() null out _resolvedGeometries while the first is still mid-loop writing into it by
    // index, throwing IndexOutOfRangeException. Only acquired here, at the public surface: BeginAsync/
    // AdvanceAsync/EndTourAsync are private and only ever called from a method that already holds it, so
    // they never re-acquire it themselves (SemaphoreSlim isn't reentrant - acquiring it twice on the same
    // logical call chain would deadlock).
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly OnboardingOverlayHost _host = new();
    private readonly OnboardingTargetLocator _locator = new();
    private bool _isSubscribed;

    private OnboardingTour? _tour;
    private int _stepIndex = -1;

    // Bounds are resolved per "segment" (a contiguous run of steps sharing a page — see
    // ResolveSegmentAsync/IsSegmentStart) rather than for the whole tour up front. AdvanceAsync always
    // checks _stepIndex against _resolvedThroughExclusive before reading this array (see below), so by
    // construction _stepIndex is already resolved whenever it's read — a null entry there unambiguously
    // means "that step's target never appeared", never "not resolved yet". AdvanceAsync skips those.
    private SpotlightGeometry?[] _resolvedGeometries = [];

    // Exclusive index up to which segments have been resolved so far. AdvanceAsync compares _stepIndex
    // against this to notice when it's about to cross into a segment that hasn't been resolved yet.
    private int _resolvedThroughExclusive;

    /// <summary>
    /// Whether a tour is currently in progress. Reads <c>_tour</c> directly rather than going through
    /// <see cref="_gate"/> — a reference read is atomic, so this can't tear or throw, but it also isn't a
    /// synchronized snapshot: the answer can be stale by the time a caller acts on it if another call
    /// (e.g. from a concurrently-running <see cref="NextAsync"/>/<see cref="SkipAsync"/>) changes
    /// <c>_tour</c> in between. Treat it as informational, not as something to branch on for correctness
    /// (the gated methods already handle that internally).
    /// </summary>
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

    /// <summary>
    /// Resets the tour's completion flag and restarts it, ending any tour currently in progress first
    /// without marking it completed — that tour was interrupted, not finished or skipped, so its own
    /// completion state (if any) is left untouched. Only the requested <paramref name="tour"/>'s
    /// completion flag is reset to false.
    /// </summary>
    public async Task ReplayTourAsync(OnboardingTour tour, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (IsTourActive)
                await EndTourAsync(markCompleted: false);

            OnboardingCompletionStore.SetCompleted(tour.Key, false);
            await BeginAsync(tour, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Advancing can now cross into a not-yet-resolved page segment (see <see cref="ResolveSegmentAsync"/>)
    /// and block on bounds resolution for up to the locator's timeout — <paramref name="ct"/> lets a
    /// caller cancel that wait, the same as <see cref="StartTourIfNotCompletedAsync"/>/<see cref="ReplayTourAsync"/> already allow.
    /// </summary>
    public async Task NextAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await AdvanceAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Also marks the tour completed — matches "Done" semantics, so it won't nag again.</summary>
    public async Task SkipAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (IsTourActive)
                await EndTourAsync(markCompleted: true);
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
        _resolvedGeometries = new SpotlightGeometry?[tour.Steps.Count];
        // Resolve the first segment before the overlay is ever shown — see ResolveSegmentOrAbandonAsync.
        _resolvedThroughExclusive = await ResolveSegmentOrAbandonAsync(0, ct);

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
            // Reached the end naturally — every step was shown (or legitimately skipped for a target
            // that never resolved), so this counts as completed the same as an explicit Skip.
            await EndTourAsync(markCompleted: true);
            return;
        }

        if (_stepIndex >= _resolvedThroughExclusive)
        {
            // Crossing into a not-yet-resolved segment (a different page). Pop the overlay first so the
            // upcoming navigate + bounds-resolution happens against a fully uncovered page — see
            // ResolveSegmentOrAbandonAsync — then re-push. ShowAsync resets the overlay's geometry on
            // every re-push, which incidentally also stops the move animation from lerping across the
            // page transition.
            await _host.HideAsync();
            _resolvedThroughExclusive = await ResolveSegmentOrAbandonAsync(_stepIndex, ct);
            await _host.ShowAsync();
        }

        SpotlightGeometry? geometry = _resolvedGeometries[_stepIndex];
        if (geometry is null)
        {
            // Target never appeared during pre-resolution — skip rather than show a bogus/empty hole.
            await AdvanceAsync(ct);
            return;
        }

        OnboardingStep step = _tour.Steps[_stepIndex];
        await _host.UpdateStepAsync(geometry, step.Title, step.Description, IsLastDisplayedStep(_stepIndex));
    }

    /// <summary>
    /// Whether <paramref name="stepIndex"/> is the last step that will actually be shown — not just the
    /// last one configured. The tooltip's Next/Done button reflects this, so a tour whose true final step
    /// never resolves (e.g. a typo'd <see cref="OnboardingStep.TargetKey"/>) shouldn't show "Next" on the
    /// step actually displayed before it just vanishing. Only ever answerable with certainty using bounds
    /// already resolved: if a later, not-yet-visited segment exists (<c>_resolvedThroughExclusive &lt;
    /// _tour.Steps.Count</c>), whether it'll display anything is genuinely unknown without resolving it —
    /// which would mean navigating there early, defeating the whole point of resolving lazily per segment
    /// (see <see cref="ResolveSegmentAsync"/>). In that case this conservatively answers false; the one
    /// residual gap is an entire trailing segment whose targets all fail to resolve, which still shows
    /// "Next" on the step before it. Narrower than the pre-segment version of this bug (any single step's
    /// target failing), and accepted for the same reason the brief page-transition flicker is.
    /// </summary>
    private bool IsLastDisplayedStep(int stepIndex)
    {
        OnboardingTour tour = _tour!;
        if (_resolvedThroughExclusive < tour.Steps.Count)
            return false;

        for (int i = stepIndex + 1; i < tour.Steps.Count; i++)
        {
            if (_resolvedGeometries[i] is not null)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Ends the active tour's runtime state and hides the overlay — separate from whether it's recorded
    /// as <em>completed</em> in <see cref="OnboardingCompletionStore"/>, since those aren't the same
    /// thing: reaching the last step or an explicit Skip both count as completed (<paramref
    /// name="markCompleted"/> <c>true</c>), but a tour ended only because <see cref="ReplayTourAsync"/>
    /// is replacing it with a different one was interrupted, not finished — its completion state (if any)
    /// should be left exactly as it was.
    /// </summary>
    private async Task EndTourAsync(bool markCompleted)
    {
        if (_tour is null)
            return;

        if (markCompleted)
            OnboardingCompletionStore.SetCompleted(_tour.Key, true);

        await _host.HideAsync();
        _tour = null;
        _stepIndex = -1;
        _resolvedGeometries = [];
        _resolvedThroughExclusive = 0;
    }

    /// <summary>
    /// Wraps <see cref="ResolveSegmentAsync"/> with cleanup: an exception partway through resolving a
    /// segment (the locator throws on cancellation — see <see cref="OnboardingTargetLocator.ResolveBoundsAsync"/>
    /// — and a bad <see cref="OnboardingStep.RequiredRoute"/> can throw straight out of
    /// <c>Shell.Current.GoToAsync</c>) would otherwise leave the coordinator in a state no caller can see
    /// or recover from: <c>_tour</c> non-null (so <see cref="IsTourActive"/> reports true and
    /// <see cref="StartTourIfNotCompletedAsync"/> permanently no-ops for it) while the overlay is hidden
    /// or was never shown, with no Skip button on screen for the user to escape through. On failure this
    /// ends the tour the same way an interrupted <see cref="ReplayTourAsync"/> does — not marked
    /// completed, since it wasn't finished or skipped — then rethrows so the caller still learns resolution
    /// failed.
    /// </summary>
    private async Task<int> ResolveSegmentOrAbandonAsync(int startIndex, CancellationToken ct)
    {
        try
        {
            return await ResolveSegmentAsync(startIndex, ct);
        }
        catch
        {
            await EndTourAsync(markCompleted: false);
            throw;
        }
    }

    /// <summary>
    /// Resolves bounds for one "segment": <paramref name="startIndex"/> plus every following step up to
    /// (not including) the next one that starts a new segment (<see cref="IsSegmentStart"/>) — navigating
    /// first via <c>Shell.Current.GoToAsync</c> if the segment's first step declares a
    /// <see cref="OnboardingStep.RequiredRoute"/> (only ever the first step: any later one with a
    /// <c>RequiredRoute</c> would itself start a new segment). Must run to completion before the overlay
    /// is shown or updated for any step in the segment it resolves — once the modal covers a page, native
    /// bounds lookups against some controls behind it (observed with a CollectionView's RecyclerView) can
    /// stall indefinitely rather than merely being delayed, regardless of how long the locator is willing
    /// to wait, so resolving only ever happens while the target page is fully uncovered. Returns the
    /// exclusive end index of the segment (the next segment's first index, or <c>tour.Steps.Count</c> if
    /// this was the last one).
    /// </summary>
    private async Task<int> ResolveSegmentAsync(int startIndex, CancellationToken ct)
    {
        OnboardingTour tour = _tour!;

        OnboardingStep firstStep = tour.Steps[startIndex];
        if (firstStep.RequiredRoute is not null && Shell.Current is not null)
            await Shell.Current.GoToAsync(firstStep.RequiredRoute);

        int end = startIndex + 1;
        while (end < tour.Steps.Count && !IsSegmentStart(tour.Steps[end]))
            end++;

        // Resolve every step in the segment concurrently rather than one at a time. Serially, a segment
        // with several targets that never appear (a typo'd TargetKey, a conditionally-hidden control)
        // would take up to (missing targets in the segment) x TargetResolveTimeout before the overlay
        // ever shows/updates for it — a tour could sit blank for tens of seconds. Concurrently, the worst
        // case is bounded by a single TargetResolveTimeout no matter how many steps in the segment are
        // missing. Safe to do: every step's TargetKey is independent (no shared mutable state in
        // OnboardingTargetLocator/OnboardingTargetRegistry across calls), and only the segment's first
        // step ever navigates, already done above before any of this starts.
        var resolutions = new Task[end - startIndex];
        for (int i = startIndex; i < end; i++)
            resolutions[i - startIndex] = ResolveStepAsync(i, ct);

        await Task.WhenAll(resolutions);
        return end;
    }

    private async Task ResolveStepAsync(int index, CancellationToken ct)
    {
        OnboardingStep step = _tour!.Steps[index];
        Rect? bounds = await _locator.ResolveBoundsAsync(step.TargetKey, TargetResolveTimeout, ct);
        _resolvedGeometries[index] = bounds is { } b
            ? new SpotlightGeometry(Inflate(b, step.SpotlightPadding), step.Shape, step.CornerRadius)
            : null; // target never appeared — AdvanceAsync skips this index
    }

    /// <summary>A segment starts at step 0, or any step with a non-null <see cref="OnboardingStep.RequiredRoute"/>.</summary>
    private static bool IsSegmentStart(OnboardingStep step) => step.RequiredRoute is not null;

    // Routed through the public NextAsync/SkipAsync (not AdvanceAsync/EndTourAsync directly) so a tooltip
    // tap is serialized by _gate the same as any other entry point - see _gate's doc comment.
    private void OnNextRequested(object? sender, EventArgs e) => _ = RunAndLogAsync(() => NextAsync());

    private void OnSkipRequested(object? sender, EventArgs e) => _ = RunAndLogAsync(() => SkipAsync());

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
