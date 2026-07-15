using System;
using System.Collections.Generic;

namespace SimpleModelServer.UI.Core
{
    /// <summary>
    /// Manages Simple Model Server settings and configuration.
    /// Can be persisted through various backends (VS, file, registry, etc).
    /// </summary>
    public class SettingsManager
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

        private readonly Dictionary<string, object> _settings = new();

        public SettingsManager()
        {
            LoadDefaults();
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
        }

        /// <summary>
        /// Gets the server host URL.
        /// </summary>
        public string ServerHost
        {
            get => GetString(ServerHostKey, "localhost");
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
            get => GetString(ApiKeyKey, string.Empty);
            set => SetValue(ApiKeyKey, value);
        }

        /// <summary>
        /// Gets the default agent type.
        /// </summary>
        public string DefaultAgentType
        {
            get => GetString(DefaultAgentTypeKey, "Ask");
            set => SetValue(DefaultAgentTypeKey, value);
        }

        /// <summary>
        /// Gets the enabled features.
        /// </summary>
        public string EnabledFeatures
        {
            get => GetString(EnabledFeaturesKey, "all");
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
            get => GetString(ChatHistoryKey, string.Empty);
            set => SetValue(ChatHistoryKey, value);
        }

        /// <summary>
        /// Path (or bare name, if on PATH) to the SimpleModelServer.Evals executable, used to
        /// run the golden set after an agent prompt change.
        /// </summary>
        public string EvalsExecutablePath
        {
            get => GetString(EvalsExecutablePathKey, "SimpleModelServer.Evals.exe");
            set => SetValue(EvalsExecutablePathKey, value);
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

        private string GetString(string key, string defaultValue)
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
    }
}
