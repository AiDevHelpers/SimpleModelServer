using SimpleModelServer.Chat.Maui.Services;
using SimpleModelServer.Discovery;

namespace SimpleModelServer.Chat.Maui;

public partial class AppShell : Shell
{
	private readonly IServerLocator _serverLocator;

	public AppShell(IServerLocator serverLocator)
	{
		_serverLocator = serverLocator ?? throw new ArgumentNullException(nameof(serverLocator));
		InitializeComponent();
		Routing.RegisterRoute("SettingsPage", typeof(Pages.SettingsPage));
	}

	private async void OnRestartServerClicked(object sender, EventArgs e)
	{
		try
		{
			var resolved = await _serverLocator.ResolveAsync();
			using var httpClient = AuthenticatedHttpClientFactory.Create(resolved);

			var reloadUri = new Uri(resolved.BaseUrl).AppendPath("/admin/reload");
			using var response = await httpClient.PostAsync(reloadUri, new StringContent("{}"));

			if (response.IsSuccessStatusCode)
			{
				await Shell.Current.DisplayAlert("Server Restart", "Restart command sent successfully", "OK");
			}
			else
			{
				await Shell.Current.DisplayAlert("Server Restart", $"Failed to restart server (status: {response.StatusCode})", "OK");
			}
		}
		catch (Exception ex)
		{
			await Shell.Current.DisplayAlert("Error", $"Failed to restart server: {ex.Message}", "OK");
		}
	}

	private async void OnSettingsClicked(object sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("SettingsPage");
	}
}
