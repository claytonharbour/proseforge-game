using Microsoft.Extensions.Logging;
using Orbit.Engine;
using Plugin.Maui.Audio;
using ProseForge.Game.Themes;
using ProseForge.Game.ViewModels;

namespace ProseForge.Game;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseOrbitEngine()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});
		builder.AddAudio();
		builder.Services.AddSingleton(TimeProvider.System);
		builder.Services.AddSingleton<SaveStore>();
		builder.Services.AddScoped<GuildHallScene>();
		builder.Services.AddSingleton<ThemeResources>();
		builder.Services.AddSingleton<VoicePlayer>();
		builder.Services.AddSingleton<ThemeHost>();
		builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
		builder.Services.AddSingleton(sp => new BookCatalogService(
			sp.GetRequiredService<HttpClient>(), FileSystem.AppDataDirectory));
		builder.Services.AddTransient<AboutPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
