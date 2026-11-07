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
		window = new Window(Navigation(mainPage));
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

	private void RefreshBooks() => _ = Task.Run(() => books.RefreshAsync());

	private MainPage NewPage(GameViewModel model) => ActivatorUtilities.CreateInstance<MainPage>(services, model);

	// The bar About shows takes the current theme's colours too.
	private NavigationPage Navigation(MainPage page) => new(page)
	{
		BarBackgroundColor = Color.FromArgb(themes.Current.Theme.Palette.Background),
		BarTextColor = Color.FromArgb(themes.Current.Theme.Palette.Text)
	};
}
