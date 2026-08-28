namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// Reads a live control's absolute on-screen bounds — the one piece of platform-specific code this
/// module needs, since MAUI only exposes a view's position relative to its parent's layout, not its
/// absolute screen position.
/// </summary>
public static class OnboardingBoundsExtensions
{
    /// <summary>
    /// Absolute on-screen bounds in device-independent units, or null if the element isn't attached to a
    /// live native view yet (not yet laid out, or already detached). Both a target's bounds and the
    /// overlay's own root bounds should be read through this same method — subtracting one from the
    /// other then cancels out any constant offset (status bar, notch/safe-area insets) automatically.
    /// </summary>
    public static Rect? GetAbsoluteBounds(this VisualElement element)
    {
#if ANDROID
        if (element.Handler?.PlatformView is not Android.Views.View nativeView
            || !nativeView.IsAttachedToWindow
            || nativeView.Width == 0
            || nativeView.Height == 0)
        {
            return null;
        }

        var location = new int[2];
        nativeView.GetLocationOnScreen(location);
        float density = nativeView.Resources?.DisplayMetrics?.Density ?? 1f;

        return new Rect(location[0] / density, location[1] / density, nativeView.Width / density, nativeView.Height / density);
#elif IOS
        if (element.Handler?.PlatformView is not UIKit.UIView nativeView || nativeView.Window == null)
            return null;

        CoreGraphics.CGRect frame = nativeView.ConvertRectToView(nativeView.Bounds, nativeView.Window);
        if (frame.Width == 0 || frame.Height == 0)
            return null;

        return new Rect(frame.X, frame.Y, frame.Width, frame.Height);
#else
        return null;
#endif
    }
}
