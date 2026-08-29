using System.Globalization;

namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// True when the bound value is non-null (used to show the tooltip's optional per-step content row only
/// when <see cref="OnboardingStep.Content"/> produced something). A tiny local converter rather than a
/// dependency on CommunityToolkit.Maui, matching <see cref="InvertedBoolConverter"/>'s precedent.
/// </summary>
public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
