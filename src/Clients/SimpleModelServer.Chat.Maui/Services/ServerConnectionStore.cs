using SimpleModelServer.Discovery;

namespace SimpleModelServer.Chat.Maui.Services;

/// <summary>
/// MAUI-based implementation of IServerConnectionStore using Preferences for configuration
/// and SecureStorage for sensitive data (API key).
/// </summary>
public class ServerConnectionStore : IServerConnectionStore
{
	private const string ModeKey = "server_connection_mode";
	private const string ManualHostKey = "server_manual_host";
	private const string ManualPortKey = "server_manual_port";
	private const string RememberedMdnsInstanceNameKey = "server_remembered_mdns_instance";
	private const string ApiKeyKey = "server_api_key";

	/// <summary>
	/// Asynchronously loads the server connection settings from Preferences and SecureStorage.
	/// </summary>
	public async Task<ServerConnectionSettings> LoadAsync(CancellationToken cancellationToken = default)
	{
		var settings = new ServerConnectionSettings();

		// Load mode (from Preferences)
		if (Enum.TryParse<ConnectionMode>(
			Preferences.Default.Get(ModeKey, ConnectionMode.Local.ToString()),
			out var mode))
		{
			settings.Mode = mode;
		}

		// Load manual host (from Preferences)
		var manualHost = Preferences.Default.Get(ManualHostKey, (string?)null);
		if (!string.IsNullOrWhiteSpace(manualHost))
		{
			settings.ManualHost = manualHost;
		}

		// Load manual port (from Preferences)
		settings.ManualPort = Preferences.Default.Get(ManualPortKey, 11435);

		// Load remembered mDNS instance name (from Preferences)
		var rememberedMdns = Preferences.Default.Get(RememberedMdnsInstanceNameKey, (string?)null);
		if (!string.IsNullOrWhiteSpace(rememberedMdns))
		{
			settings.RememberedMdnsInstanceName = rememberedMdns;
		}

		// Load API key (from SecureStorage)
		try
		{
			var apiKey = await SecureStorage.Default.GetAsync(ApiKeyKey);
			if (!string.IsNullOrWhiteSpace(apiKey))
			{
				settings.ApiKey = apiKey;
			}
		}
		catch
		{
			// SecureStorage may not be available on all platforms; gracefully fall back
		}

		return settings;
	}

	/// <summary>
	/// Asynchronously saves the server connection settings to Preferences and SecureStorage.
	/// </summary>
	public async Task SaveAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(settings);

		// Save mode (to Preferences)
		Preferences.Default.Set(ModeKey, settings.Mode.ToString());

		// Save manual host (to Preferences)
		if (!string.IsNullOrWhiteSpace(settings.ManualHost))
		{
			Preferences.Default.Set(ManualHostKey, settings.ManualHost);
		}
		else
		{
			Preferences.Default.Remove(ManualHostKey);
		}

		// Save manual port (to Preferences)
		Preferences.Default.Set(ManualPortKey, settings.ManualPort);

		// Save remembered mDNS instance name (to Preferences)
		if (!string.IsNullOrWhiteSpace(settings.RememberedMdnsInstanceName))
		{
			Preferences.Default.Set(RememberedMdnsInstanceNameKey, settings.RememberedMdnsInstanceName);
		}
		else
		{
			Preferences.Default.Remove(RememberedMdnsInstanceNameKey);
		}

		// Save API key (to SecureStorage)
		try
		{
			if (!string.IsNullOrWhiteSpace(settings.ApiKey))
			{
				await SecureStorage.Default.SetAsync(ApiKeyKey, settings.ApiKey);
			}
			else
			{
				SecureStorage.Default.Remove(ApiKeyKey);
			}
		}
		catch
		{
			// SecureStorage may not be available on all platforms; gracefully fall back
		}

		await Task.CompletedTask;
	}
}
