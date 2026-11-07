using Microsoft.Extensions.DependencyInjection;

namespace SimpleModelServer.Chat.Maui;

public partial class App : Application
{
	private readonly IServiceProvider _serviceProvider;

	public App(IServiceProvider serviceProvider)
	{
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var appShell = _serviceProvider.GetRequiredService<AppShell>();
		return new Window(appShell);
	}
}
