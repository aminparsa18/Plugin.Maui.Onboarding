using System.Globalization;

namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// Flips a bool for a binding (used to hide the tooltip's Skip button on the tour's last step).
/// A tiny local converter rather than a dependency on CommunityToolkit.Maui, matching
/// Plugin.Maui.Shimmer's precedent of no dependency beyond Microsoft.Maui.Controls.
/// </summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value!;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value!;
}
