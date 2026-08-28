namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// Resolves a registered target's absolute on-screen bounds, polling until it's attached, visible, and
/// laid out (or the timeout elapses). Covers both "the control hasn't been constructed yet" and "a Shell
/// tab switch is still in flight" — the coordinator doesn't need to reason about either case separately.
/// </summary>
public sealed class OnboardingTargetLocator
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(75);

    /// <summary>Returns null on timeout — the caller should skip the step rather than hang the tour.</summary>
    public async Task<Rect?> ResolveBoundsAsync(string targetKey, TimeSpan timeout, CancellationToken ct = default)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        Rect? previousCandidate = null;

        while (DateTime.UtcNow < deadline)
        {
            bool found = OnboardingTargetRegistry.TryGet(targetKey, out VisualElement? element);
            bool hasHandler = found && element?.Handler?.PlatformView is not null;
            bool visible = hasHandler && IsVisibleInHierarchy(element!);
            Rect? candidate = visible ? element!.GetAbsoluteBounds() : null;

            if (candidate is { Width: > 0, Height: > 0 } bounds)
            {
                // Require two consecutive matching reads before trusting the position. A view inside a
                // scroll/virtualizing container (e.g. CollectionView's native RecyclerView) can report a
                // valid non-zero size from an early measure pass while its on-screen position hasn't
                // settled yet (still reporting a stale/(0,0) location) — accepting the very first
                // non-zero-size read alone would spotlight the wrong spot on the first poll.
                if (previousCandidate is { } previous && IsClose(previous, bounds))
                    return bounds;

                previousCandidate = bounds;
            }
            // else: leave previousCandidate as-is. A momentary null (e.g. a mid-relayout frame where the
            // native view's measured size briefly reads 0 — observed happening repeatedly for several
            // seconds on a CollectionView whose content is still settling) shouldn't discard an already
            // ­accepted candidate and restart the two-consecutive-reads count from scratch.

            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool IsClose(Rect a, Rect b, double tolerance = 1.0)
        => Math.Abs(a.X - b.X) < tolerance
           && Math.Abs(a.Y - b.Y) < tolerance
           && Math.Abs(a.Width - b.Width) < tolerance
           && Math.Abs(a.Height - b.Height) < tolerance;

    private static bool IsVisibleInHierarchy(VisualElement element)
    {
        Element? current = element;
        while (current is VisualElement visual)
        {
            if (!visual.IsVisible)
                return false;

            current = current.Parent;
        }

        return true;
    }
}
