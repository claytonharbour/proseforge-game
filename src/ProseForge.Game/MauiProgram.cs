using Microsoft.Extensions.Logging;
using Orbit.Engine;
using Plugin.Maui.Audio;

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
		builder.Services.AddSingleton<GuildStateStore>();
		builder.Services.AddSingleton(sp => sp.GetRequiredService<GuildStateStore>().Load());
		builder.Services.AddScoped<GuildHallScene>();
		builder.Services.AddTransient<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
