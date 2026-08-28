namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// Keyed registry of on-screen controls that can be spotlighted, populated by
/// <see cref="OnboardingTargetBehavior"/> as pages/components attach and detach. Weakly referenced so
/// registering a target never keeps its page alive after it's been navigated away from.
/// </summary>
public static class OnboardingTargetRegistry
{
    private static readonly Dictionary<string, WeakReference<VisualElement>> Targets = [];

    public static void Register(string key, VisualElement element) => Targets[key] = new WeakReference<VisualElement>(element);

    public static void Unregister(string key, VisualElement element)
    {
        if (Targets.TryGetValue(key, out WeakReference<VisualElement>? existing)
            && existing.TryGetTarget(out VisualElement? current)
            && ReferenceEquals(current, element))
        {
            Targets.Remove(key);
        }
    }

    public static bool TryGet(string key, out VisualElement? element)
    {
        element = null;
        if (!Targets.TryGetValue(key, out WeakReference<VisualElement>? reference))
            return false;

        if (reference.TryGetTarget(out VisualElement? target))
        {
            element = target;
            return true;
        }

        Targets.Remove(key);
        return false;
    }
}
