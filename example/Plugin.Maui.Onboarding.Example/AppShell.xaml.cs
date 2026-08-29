namespace Plugin.Maui.Onboarding.Example;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Not a ShellContent/flyout entry - only reachable via GoToAsync, e.g. the onboarding tour's
		// RequiredRoute (see MainPage.xaml.cs BuildTour). Standard Shell pattern for a page outside the
		// flyout/tab structure.
		Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
	}
}
