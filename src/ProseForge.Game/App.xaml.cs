using ProseForge.Game.Themes;
using ProseForge.Game.ViewModels;

namespace ProseForge.Game;

public partial class App : Application
{
	private readonly ThemeHost themes;
	private readonly IServiceProvider services;
	private readonly BookCatalogService books;
	private MainPage mainPage;
	private Window? window;

	public App(ThemeHost themes, IServiceProvider services, BookCatalogService books)
	{
		InitializeComponent();
		this.themes = themes;
		this.services = services;
		this.books = books;
		// The palette's colours exist before any page asks for them.
		themes.ApplyPalette();
		mainPage = NewPage(themes.Current);
		// A theme switch puts a page built on the new theme in the window: no restart.
		themes.Switched += model =>
		{
			mainPage = NewPage(model);
			if (window is not null) window.Page = Navigation(mainPage);
		};
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		window = new Window(Navigation(mainPage)) { Title = "One More Story" };
#if WINDOWS
		// On a desktop the game opens phone-shaped and centred, and can't shrink below a size that keeps
		// every bar on screen. The sizes come from ScreenLayout, which the tests check.
		var display = DeviceDisplay.Current.MainDisplayInfo;
		var size = ScreenLayout.DesktopWindow(display.Width / display.Density, display.Height / display.Density,
			themes.Current.Rules.Stations.Count);
		(window.Width, window.Height, window.MinimumWidth, window.MinimumHeight, window.X, window.Y) =
			(size.Width, size.Height, size.MinWidth, size.MinHeight, size.X, size.Y);
#elif MACCATALYST
		// Mac Catalyst ignores Window.Width/Height and the minimums. Measured on this Mac:
		// - a limit set while the scene connects is reset by MAUI once the window exists, so it goes on
		//   after the window is created;
		// - the scene's units are iPad points, drawn at 77% on a Mac ("Scale Interface to Match iPad"):
		//   a 360x518 minimum let the window shrink to 278x399 on screen. So sizes are worked out in Mac
		//   points and divided by 0.77.
		// The default size is requested once, on the first launch; after that macOS keeps the player's size.
		window.Created += (_, _) => window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () =>
		{
			if (window.Handler?.PlatformView is not UIKit.UIWindow { WindowScene: { SizeRestrictions: { } limits } scene } ui) return;
			const double MacScale = 0.77;
			var size = ScreenLayout.DesktopWindow(scene.Screen.Bounds.Width * MacScale, scene.Screen.Bounds.Height * MacScale,
				themes.Current.Rules.Stations.Count);
			limits.MinimumSize = new CoreGraphics.CGSize(size.MinWidth / MacScale, size.MinHeight / MacScale);
			const string SizedKey = "desktop.sized.v1";
			if (Preferences.Default.Get(SizedKey, false) || !OperatingSystem.IsMacCatalystVersionAtLeast(16)) return;
			var wanted = new CoreGraphics.CGRect(size.X / MacScale, size.Y / MacScale, size.Width / MacScale, size.Height / MacScale);
			scene.RequestGeometryUpdate(new UIKit.UIWindowSceneGeometryPreferencesMac(wanted),
				error => System.Diagnostics.Debug.WriteLine($"[desktop] window size refused: {error.LocalizedDescription}"));
			// Remember it only once the window really is that size: if macOS refused, or the
			// request didn't take, the next launch tries again instead of never sizing the window.
			window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
			{
				// The request is in macOS points but ui.Frame is in iPad points (÷0.77), so scale it back.
				// Within 2%, because macOS snaps the frame a few points.
				var reached = Math.Abs(ui.Frame.Width * MacScale / wanted.Width - 1) < 0.02
					&& Math.Abs(ui.Frame.Height * MacScale / wanted.Height - 1) < 0.02;
				if (reached)
					Preferences.Default.Set(SizedKey, true);
#if DEBUG
				sizeCheck = $"check frame {ui.Frame.Size} wanted {wanted.Size} reached {reached}";
#endif
			});
		});
#endif
#if DEBUG && MACCATALYST
		// Debug builds on a Mac save a picture of their own window every two seconds, so the
		// desktop layout can be checked without a screen-recording permission. Never in a release.
		window.Dispatcher.StartTimer(TimeSpan.FromSeconds(2), () =>
		{
			_ = SaveDebugShotAsync();
			return true;
		});
#endif
		window.Stopped += (_, _) => mainPage.PauseForBackground();
		window.Resumed += (_, _) =>
		{
			mainPage.Foreground();
			if (books.NeedsRefresh) RefreshBooks();
		};
		// The About page's books load in the background: a few seconds after start, so the
		// game is on screen first, then every half hour while the app is open.
		window.Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(4), RefreshBooks);
		window.Dispatcher.StartTimer(BookCatalogService.StaleAfter, () =>
		{
			RefreshBooks();
			return true;
		});
		return window;
	}

#if DEBUG && MACCATALYST
	private string sizeCheck = "check not run";
	private string SceneLimits() => window?.Handler?.PlatformView is UIKit.UIWindow ui
		? ui.WindowScene is { } sc
			? $"scene min {sc.SizeRestrictions?.MinimumSize} max {sc.SizeRestrictions?.MaximumSize} frame {ui.Frame.Size}"
			: "no scene"
		: $"platform view {window?.Handler?.PlatformView?.GetType().Name ?? "none"}";

	private async Task SaveDebugShotAsync()
	{
		try
		{
			if (window is null) return;
			if (!Screenshot.Default.IsCaptureSupported)
			{
				await File.WriteAllTextAsync(Path.Combine(FileSystem.AppDataDirectory, "debug-shot.txt"), "capture not supported\n");
				return;
			}
			var shot = await Screenshot.Default.CaptureAsync();
			await using var image = await shot.OpenReadAsync(ScreenshotFormat.Png);
			var folder = FileSystem.AppDataDirectory;
			await using (var file = File.Create(Path.Combine(folder, "debug-shot.png"))) await image.CopyToAsync(file);
			await File.WriteAllTextAsync(Path.Combine(folder, "debug-shot.txt"),
				$"{DateTime.UtcNow:O} window {window.Width:0}x{window.Height:0} shot {shot.Width}x{shot.Height} {SceneLimits()} {sizeCheck}\n");
		}
		catch (Exception e)
		{
			// A debugging aid: if it fails, the game carries on, and says why.
			try { await File.WriteAllTextAsync(Path.Combine(FileSystem.AppDataDirectory, "debug-shot.txt"), $"failed: {e}\n"); }
			catch (Exception) { }
		}
	}
#endif

	private void RefreshBooks() => _ = Task.Run(() => books.RefreshAsync());

	private MainPage NewPage(GameViewModel model) => ActivatorUtilities.CreateInstance<MainPage>(services, model);

	// The bar About shows takes the current theme's colours too.
	private NavigationPage Navigation(MainPage page) => new(page)
	{
		BarBackgroundColor = Color.FromArgb(themes.Current.Theme.Palette.Background),
		BarTextColor = Color.FromArgb(themes.Current.Theme.Palette.Text)
	};
}
