using Microsoft.Extensions.Logging;
using SimpleModelServer.Chat.Maui.Services;
using SimpleModelServer.Chat.Maui.Pages;
using SimpleModelServer.Discovery;

namespace SimpleModelServer.Chat.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
			.Services
				.AddSingleton<IServerConnectionStore, ServerConnectionStore>()
				.AddSingleton<IServerLocator, ServerLocator>()
				.AddSingleton<ChatService>()
				.AddSingleton<AppShell>()
				.AddSingleton<App>()
				.AddTransient<ChatPage>()
				.AddTransient<Pages.SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
