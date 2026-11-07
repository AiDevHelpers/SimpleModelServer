using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SimpleModelServer.Discovery;

namespace SimpleModelServer.UI.Core
{
	/// <summary>
	/// Manages Simple Model Server settings and configuration.
	/// Provides file-based persistence and integration with discovery library.
	/// </summary>
	public class SettingsManager : IDisposable
	{
		// Settings keys
		public const string SettingsCollectionPath = "SimpleModelServer";

		private const string ServerHostKey = "ServerHost";
		private const string ServerPortKey = "ServerPort";
		private const string ApiKeyKey = "ApiKey";
		private const string DefaultAgentTypeKey = "DefaultAgentType";
		private const string EnabledFeaturesKey = "EnabledFeatures";
		private const string RememberLastChatKey = "RememberLastChat";
		private const string ChatHistoryKey = "ChatHistory";
		private const string EvalsExecutablePathKey = "EvalsExecutablePath";
		private const string ConnectionModeKey = "ConnectionMode";
		private const string RememberedMdnsInstanceNameKey = "RememberedMdnsInstanceName";

		private readonly Dictionary<string, object> _settings = new();
		private readonly string _settingsFilePath;
		private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
		private bool _disposed;

		/// <summary>
		/// Creates a SettingsManager with the default settings file location.
		/// </summary>
		public SettingsManager() : this(null)
		{
		}

		/// <summary>
		/// Creates a SettingsManager with a custom settings file location (useful for testing).
		/// </summary>
		/// <param name="customSettingsFilePath">Custom path to settings file, or null to use default.</param>
		public SettingsManager(string? customSettingsFilePath = null)
		{
			_settingsFilePath = customSettingsFilePath ?? GetDefaultSettingsFilePath();
			LoadDefaults();
			// Try to load persisted settings asynchronously in background after defaults are set
			_ = LoadAsyncInternal();
		}

		private void LoadDefaults()
		{
			// Default settings
			_settings[ServerHostKey] = "localhost";
			_settings[ServerPortKey] = 11434;
			_settings[ApiKeyKey] = string.Empty;
			_settings[DefaultAgentTypeKey] = "Ask";
			_settings[EnabledFeaturesKey] = "all";
			_settings[RememberLastChatKey] = true;
			_settings[ChatHistoryKey] = string.Empty;
			_settings[EvalsExecutablePathKey] = "SimpleModelServer.Evals.exe";
			_settings[ConnectionModeKey] = ConnectionMode.Local;
			_settings[RememberedMdnsInstanceNameKey] = string.Empty;
		}

		private static string GetDefaultSettingsFilePath()
		{
			var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			var settingsDir = Path.Combine(appDataDir, "SimpleModelServer");
			Directory.CreateDirectory(settingsDir);
			return Path.Combine(settingsDir, "vs-extension-settings.json");
		}

		private async Task LoadAsyncInternal()
		{
			try
			{
				await LoadAsync(CancellationToken.None);
			}
			catch (Exception)
			{
				// Silently ignore load failures — defaults are already set
			}
		}

		/// <summary>
		/// Asynchronously loads settings from the persistent storage file.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		public async Task LoadAsync(CancellationToken cancellationToken = default)
		{
			await _fileLock.WaitAsync(cancellationToken);
			try
			{
				if (!File.Exists(_settingsFilePath))
				{
					return;
				}

				var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
				if (string.IsNullOrWhiteSpace(json))
				{
					return;
				}

				using var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;

				// Load each setting from JSON
				if (root.TryGetProperty(ServerHostKey, out var hostEl))
				{
					_settings[ServerHostKey] = hostEl.GetString() ?? "localhost";
				}

				if (root.TryGetProperty(ServerPortKey, out var portEl) && portEl.TryGetInt32(out var port))
				{
					_settings[ServerPortKey] = port;
				}

				if (root.TryGetProperty(ApiKeyKey, out var keyEl))
				{
					_settings[ApiKeyKey] = keyEl.GetString() ?? string.Empty;
				}

				if (root.TryGetProperty(DefaultAgentTypeKey, out var agentEl))
				{
					_settings[DefaultAgentTypeKey] = agentEl.GetString() ?? "Ask";
				}

				if (root.TryGetProperty(EnabledFeaturesKey, out var featuresEl))
				{
					_settings[EnabledFeaturesKey] = featuresEl.GetString() ?? "all";
				}

				if (root.TryGetProperty(RememberLastChatKey, out var rememberEl) && rememberEl.ValueKind == JsonValueKind.True)
				{
					_settings[RememberLastChatKey] = rememberEl.GetBoolean();
				}

				if (root.TryGetProperty(ChatHistoryKey, out var historyEl))
				{
					_settings[ChatHistoryKey] = historyEl.GetString() ?? string.Empty;
				}

				if (root.TryGetProperty(EvalsExecutablePathKey, out var evalsEl))
				{
					_settings[EvalsExecutablePathKey] = evalsEl.GetString() ?? "SimpleModelServer.Evals.exe";
				}

				// Load discovery-related settings
				if (root.TryGetProperty(ConnectionModeKey, out var modeEl) &&
					Enum.TryParse<ConnectionMode>(modeEl.GetString(), out var mode))
				{
					_settings[ConnectionModeKey] = mode;
				}

				if (root.TryGetProperty(RememberedMdnsInstanceNameKey, out var mdnsEl))
				{
					_settings[RememberedMdnsInstanceNameKey] = mdnsEl.GetString() ?? string.Empty;
				}
			}
			finally
			{
				_fileLock.Release();
			}
		}

		/// <summary>
		/// Asynchronously saves the current settings to persistent storage.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		public async Task SaveAsync(CancellationToken cancellationToken = default)
		{
			await _fileLock.WaitAsync(cancellationToken);
			try
			{
				var dict = new Dictionary<string, object>();

				// Serialize all settings to a dictionary
				foreach (var kvp in _settings)
				{
					dict[kvp.Key] = kvp.Value switch
					{
						Enum e => e.ToString(),
						_ => kvp.Value
					};
				}

				var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
				var directory = Path.GetDirectoryName(_settingsFilePath);
				if (directory != null && !Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}

				await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken);
			}
			finally
			{
				_fileLock.Release();
			}
		}

		/// <summary>
		/// Gets the server host URL.
		/// </summary>
		public string ServerHost
		{
			get => GetString(ServerHostKey, "localhost") ?? "localhost";
			set => SetValue(ServerHostKey, value);
		}

		/// <summary>
		/// Gets the server port.
		/// </summary>
		public int ServerPort
		{
			get => GetInt(ServerPortKey, 11434);
			set => SetValue(ServerPortKey, value);
		}

		/// <summary>
		/// Gets the API key for authentication.
		/// </summary>
		public string ApiKey
		{
			get => GetString(ApiKeyKey, string.Empty) ?? string.Empty;
			set => SetValue(ApiKeyKey, value);
		}

		/// <summary>
		/// Gets the default agent type.
		/// </summary>
		public string DefaultAgentType
		{
			get => GetString(DefaultAgentTypeKey, "Ask") ?? "Ask";
			set => SetValue(DefaultAgentTypeKey, value);
		}

		/// <summary>
		/// Gets the enabled features.
		/// </summary>
		public string EnabledFeatures
		{
			get => GetString(EnabledFeaturesKey, "all") ?? "all";
			set => SetValue(EnabledFeaturesKey, value);
		}

		/// <summary>
		/// Gets whether to remember the last chat.
		/// </summary>
		public bool RememberLastChat
		{
			get => GetBool(RememberLastChatKey, true);
			set => SetValue(RememberLastChatKey, value);
		}

		/// <summary>
		/// Gets the chat history.
		/// </summary>
		public string ChatHistory
		{
			get => GetString(ChatHistoryKey, string.Empty) ?? string.Empty;
			set => SetValue(ChatHistoryKey, value);
		}

		/// <summary>
		/// Path (or bare name, if on PATH) to the SimpleModelServer.Evals executable, used to
		/// run the golden set after an agent prompt change.
		/// </summary>
		public string EvalsExecutablePath
		{
			get => GetString(EvalsExecutablePathKey, "SimpleModelServer.Evals.exe") ?? "SimpleModelServer.Evals.exe";
			set => SetValue(EvalsExecutablePathKey, value);
		}

		/// <summary>
		/// Gets or sets the connection mode (Local, Mdns, or Manual).
		/// </summary>
		public ConnectionMode ConnectionMode
		{
			get
			{
				if (_settings.TryGetValue(ConnectionModeKey, out var value) && value is ConnectionMode mode)
				{
					return mode;
				}
				return ConnectionMode.Local;
			}
			set => SetValue(ConnectionModeKey, value);
		}

		/// <summary>
		/// Gets or sets the remembered mDNS instance name.
		/// </summary>
		public string? RememberedMdnsInstanceName
		{
			get => GetString(RememberedMdnsInstanceNameKey, null);
			set => SetValue(RememberedMdnsInstanceNameKey, value ?? string.Empty);
		}

		/// <summary>
		/// Gets the full server URL.
		/// </summary>
		public string GetServerUrl()
		{
			return $"http://{ServerHost}:{ServerPort}";
		}

		/// <summary>
		/// Gets the chat endpoint URL.
		/// </summary>
		public string GetChatEndpoint()
		{
			return $"{GetServerUrl()}/api/generate";
		}

		private void SetValue(string key, object value)
		{
			_settings[key] = value;
		}

		private string? GetString(string key, string? defaultValue)
		{
			return _settings.TryGetValue(key, out var value) && value is string strValue
				? strValue
				: defaultValue;
		}

		private int GetInt(string key, int defaultValue)
		{
			if (_settings.TryGetValue(key, out var value))
			{
				return value switch
				{
					int intValue => intValue,
					string strValue when int.TryParse(strValue, out var parsed) => parsed,
					_ => defaultValue
				};
			}
			return defaultValue;
		}

		private bool GetBool(string key, bool defaultValue)
		{
			if (_settings.TryGetValue(key, out var value))
			{
				return value switch
				{
					bool boolValue => boolValue,
					string strValue when bool.TryParse(strValue, out var parsed) => parsed,
					_ => defaultValue
				};
			}
			return defaultValue;
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_fileLock?.Dispose();
			_disposed = true;
			GC.SuppressFinalize(this);
		}
	}
}
