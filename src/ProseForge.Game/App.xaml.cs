namespace ProseForge.Game;

public partial class App : Application
{
	private readonly MainPage mainPage;

	public App(MainPage mainPage)
	{
		InitializeComponent();
		this.mainPage = mainPage;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(mainPage);
		window.Stopped += (_, _) => mainPage.PauseForBackground();
		window.Resumed += (_, _) => mainPage.Foreground();
		return window;
	}
}
