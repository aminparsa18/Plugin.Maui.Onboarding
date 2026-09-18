using Plugin.Maui.Onboarding.Views;
#if IOS
using Microsoft.Maui.Platform;
#endif

namespace Plugin.Maui.Onboarding.Internals;

/// <summary>
/// Owns the single <see cref="OnboardingHostPage"/> for the app's lifetime — lazily created on the first
/// tour, shown once per tour and popped when the tour ends, reused as-is by every later tour. On Android
/// this goes through <c>Shell.Current.Navigation.PushModalAsync</c>/<c>PopModalAsync</c> (not Mopups — see
/// <see cref="OnboardingCoordinator"/>); on iOS it presents the page's native view directly — see the
/// class doc on the <c>#if IOS</c> members below for why.
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

#if IOS
    // iOS-only: Shell.Current.Navigation.PushModalAsync was confirmed (via extensive on-device
    // diagnostics across multiple host apps/iOS versions) to silently fail to create a native
    // handler for the pushed page in some Shell configurations — ModalStack.Count updates, but
    // UIKit's presentViewController: never actually gets called, with no console warning. This
    // matches longstanding, unresolved upstream MAUI defects (dotnet/maui #11745, #19225): "
    // PushModalAsync succeeds at the data-model level but never renders on iOS" — closed without a
    // fix, no known trigger condition, and the commonly-suggested NavigationPage-wrapper workaround
    // doesn't help either. A raw UIKit presentViewController: call was confirmed to work reliably
    // in the same failing app, so the overlay is presented natively on iOS, bypassing Shell's
    // modal-navigation manager entirely. Both the platform view and its wrapping UIViewController
    // are lazily created once and reused for the app's lifetime, same as _page.
    private UIKit.UIView? _nativeView;
    private UIKit.UIViewController? _nativeContainer;
#endif

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
#if IOS
                await PresentNativeAsync(_page);
#else
                Shell shell = Shell.Current ?? throw new InvalidOperationException(
                    "Plugin.Maui.Onboarding requires an active Shell (Shell.Current was null).");

                await shell.Navigation.PushModalAsync(_page, animated: false);
#endif
                _isPushed = true;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <exception cref="InvalidOperationException"><see cref="Shell.Current"/> is null while a tour's
    /// overlay is still pushed - same rationale as <see cref="ShowAsync"/> (Android/other platforms only;
    /// see the class doc for why iOS doesn't go through Shell at all).</exception>
    public async Task HideAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (!_isPushed)
                return;

#if IOS
            await DismissNativeAsync();
#else
            Shell shell = Shell.Current ?? throw new InvalidOperationException(
                "Plugin.Maui.Onboarding requires an active Shell (Shell.Current was null) to dismiss the " +
                "onboarding overlay.");

            await shell.Navigation.PopModalAsync(animated: false);
#endif
            _isPushed = false;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

#if IOS
    /// <summary>
    /// Lazily creates (once, reused for the app's lifetime) a native <see cref="UIKit.UIView"/> for
    /// <paramref name="page"/> via MAUI's public embedding API (<c>IView.ToHandler</c>), wraps it in a
    /// fresh transparent, full-screen <see cref="UIKit.UIViewController"/>, and presents that directly on
    /// the topmost currently-presented view controller of the key window — see the class doc for why this
    /// bypasses <c>Shell.Current.Navigation.PushModalAsync</c> entirely on iOS. A fresh container is used
    /// on every call (cheap, and avoids re-presenting an already-presented/possibly-torn-down
    /// UIViewController instance across tours); only the underlying native view is cached, since
    /// re-creating the handler each time would tear down and rebuild the whole overlay's platform tree.
    /// </summary>
    private Task PresentNativeAsync(Views.OnboardingHostPage page)
    {
        if (_nativeView is null)
        {
            IMauiContext mauiContext = Application.Current?.Windows.FirstOrDefault()?.Handler?.MauiContext
                ?? throw new InvalidOperationException(
                    "Plugin.Maui.Onboarding could not find a MauiContext to present the onboarding overlay on iOS.");
            _nativeView = page.ToHandler(mauiContext).PlatformView
                ?? throw new InvalidOperationException(
                    "Plugin.Maui.Onboarding could not create a native view for the onboarding overlay on iOS.");
        }

        var container = new UIKit.UIViewController { ModalPresentationStyle = UIKit.UIModalPresentationStyle.OverFullScreen };
        container.View!.BackgroundColor = UIKit.UIColor.Clear;
        container.View.AddSubview(_nativeView);
        _nativeView.Frame = container.View.Bounds;
        _nativeView.AutoresizingMask = UIKit.UIViewAutoresizing.FlexibleWidth | UIKit.UIViewAutoresizing.FlexibleHeight;
        _nativeContainer = container;

        UIKit.UIWindow[] windows = UIKit.UIApplication.SharedApplication.Windows;
        UIKit.UIWindow? keyWindow = windows.FirstOrDefault(w => w.IsKeyWindow) ?? UIKit.UIApplication.SharedApplication.KeyWindow;
        UIKit.UIViewController? presenter = keyWindow?.RootViewController;
        while (presenter?.PresentedViewController is not null)
            presenter = presenter.PresentedViewController;

        if (presenter is null)
            throw new InvalidOperationException(
                "Plugin.Maui.Onboarding could not find a view controller to present the onboarding overlay from.");

        var tcs = new TaskCompletionSource();
        presenter.PresentViewController(container, animated: false, completionHandler: () => tcs.TrySetResult());
        return tcs.Task;
    }

    /// <summary>Dismisses the <see cref="UIKit.UIViewController"/> <see cref="PresentNativeAsync"/> most
    /// recently presented. The native view itself (<see cref="_nativeView"/>) is left alone — UIKit moves
    /// it to the next container's view automatically on the following <see cref="PresentNativeAsync"/>
    /// call, same as any <see cref="UIKit.UIView"/> re-added to a new superview.</summary>
    private Task DismissNativeAsync()
    {
        if (_nativeContainer is null)
            return Task.CompletedTask;

        UIKit.UIViewController container = _nativeContainer;
        _nativeContainer = null;

        var tcs = new TaskCompletionSource();
        container.DismissViewController(animated: false, completionHandler: () => tcs.TrySetResult());
        return tcs.Task;
    }
#endif

    public Task UpdateStepAsync(SpotlightGeometry geometry, string title, string description, bool isLastStep, Func<View>? content)
        => _page?.Overlay.UpdateStepAsync(geometry, title, description, isLastStep, content) ?? Task.CompletedTask;
}
