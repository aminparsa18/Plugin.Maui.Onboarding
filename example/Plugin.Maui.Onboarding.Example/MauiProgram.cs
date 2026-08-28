using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Onboarding.Example;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			// Required by the library: OnboardingOverlayView uses CommunityToolkit.Maui's
			// FadeToAsync, and OnboardingCoordinator checks Mopups' popup stack before starting a tour.
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (h, v) =>
        {
#if ANDROID
            h.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#endif
#if IOS
            h.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
