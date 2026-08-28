using System.Text.Json;

namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// Self-contained per-tour completion persistence, backed directly by <see cref="Preferences"/>
/// (its own key, one JSON <c>Dictionary&lt;string,bool&gt;</c> blob) — no dependency on any
/// host-app persistence abstraction.
/// </summary>
public static class OnboardingCompletionStore
{
    private const string PreferenceKey = "OnboardingTourCompletion";

    public static bool IsCompleted(string tourKey) => GetMap().TryGetValue(tourKey, out bool completed) && completed;

    public static void SetCompleted(string tourKey, bool completed)
    {
        Dictionary<string, bool> map = GetMap();
        map[tourKey] = completed;
        Preferences.Set(PreferenceKey, JsonSerializer.Serialize(map));
    }

    private static Dictionary<string, bool> GetMap()
    {
        string? json = Preferences.Get(PreferenceKey, (string?)null);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
