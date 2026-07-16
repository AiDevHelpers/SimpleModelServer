using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SimpleModelServer.Discovery;

namespace SimpleModelServer.UI.Core
{
    /// <summary>
    /// Service for communicating with Simple Model Server API.
    /// Handles message sending, response streaming, and model operations.
    /// Uses the discovery library for server location resolution.
    /// </summary>
    public sealed class ChatService : IDisposable
    {
        private readonly SettingsManager _settings;
        private readonly HttpClient _httpClient;
        private readonly IServerLocator? _serverLocator;
        private readonly DiagnosticLogger _logger;
        private readonly bool _ownsHttpClient;
        private ResolvedServerConnection? _cachedResolution;
        private DateTime _resolutionTime;

        /// <summary>
        /// Creates a ChatService with optional discovery support.
        /// </summary>
        /// <param name="settings">The settings manager.</param>
        /// <param name="serverLocator">Optional server locator for discovery-based resolution.</param>
        /// <param name="httpClient">Optional HTTP client.</param>
        /// <param name="logger">Optional diagnostic logger.</param>
        public ChatService(SettingsManager settings, IServerLocator? serverLocator = null, HttpClient? httpClient = null, DiagnosticLogger? logger = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _serverLocator = serverLocator;
            _httpClient = httpClient ?? new HttpClient();
            _ownsHttpClient = httpClient == null;
            _logger = logger ?? new DiagnosticLogger("ChatService");
            _resolutionTime = DateTime.MinValue;
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Resolves the server URL, either via discovery or fallback to manual settings.
        /// Caches the result for 30 seconds to avoid repeated discovery calls.
        /// </summary>
        private async Task<string> ResolveServerUrlAsync(CancellationToken cancellationToken = default)
        {
            // Return cached result if fresh (within 30 seconds)
            if (_cachedResolution != null && (DateTime.UtcNow - _resolutionTime).TotalSeconds < 30)
            {
                return _cachedResolution.BaseUrl;
            }

            // Try to use discovery if available
            if (_serverLocator != null)
            {
                try
                {
                    _cachedResolution = await _serverLocator.ResolveAsync(cancellationToken);
                    _resolutionTime = DateTime.UtcNow;
                    return _cachedResolution.BaseUrl;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Server discovery failed, falling back to manual settings: {ex.Message}");
                }
            }

            // Fallback to manual settings
            _cachedResolution = null;
            return _settings.GetServerUrl();
        }

        /// <summary>
        /// Tests the connection to the Simple Model Server.
        /// </summary>
        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var baseUrl = await ResolveServerUrlAsync(cancellationToken);
                var healthUrl = $"{baseUrl}/health";
                using var request = new HttpRequestMessage(HttpMethod.Get, healthUrl);
                AddApiKeyIfConfigured(request, _cachedResolution);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.Error($"Connection test failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sends a message to the model and streams the response.
        /// </summary>
        public async IAsyncEnumerable<string> SendMessageAsync(
            string message,
            string agentType,
            string selectedModel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string? baseUrl;
            string? resolutionError = null;

            try
            {
                baseUrl = await ResolveServerUrlAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to resolve server: {ex.Message}");
                resolutionError = $"Error: Failed to resolve server - {ex.Message}";
                baseUrl = null;
            }

            if (resolutionError != null)
            {
                yield return resolutionError;
                yield break;
            }

            var url = $"{baseUrl}/api/generate";

            var request = new
            {
                prompt = message,
                model = selectedModel,
                stream = true,
                agent_type = agentType
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(request),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            AddApiKeyIfConfigured(httpRequest, _cachedResolution);

            HttpResponseMessage? response = null;
            Stream? stream = null;
            StreamReader? reader = null;
            string? failureMessage = null;

            try
            {
                response = await _httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    failureMessage = $"Error: Server returned {response.StatusCode}";
                }
                else
                {
                    stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    reader = new StreamReader(stream);
                }
            }
            catch (OperationCanceledException)
            {
                failureMessage = "[Request cancelled]";
            }
            catch (HttpRequestException ex)
            {
                _logger.Error($"HTTP request failed: {ex.Message}");
                failureMessage = $"Error: {ex.Message}";
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error: {ex.Message}");
                failureMessage = $"Error: {ex.Message}";
            }

            if (failureMessage != null)
            {
                response?.Dispose();
                yield return failureMessage;
                yield break;
            }

            try
            {
                string? line;
                while ((line = await reader!.ReadLineAsync(cancellationToken)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var content = ParseResponseLine(line);
                    if (!string.IsNullOrEmpty(content))
                    {
                        yield return content;
                    }
                }
            }
            finally
            {
                reader?.Dispose();
                stream?.Dispose();
                response?.Dispose();
            }
        }

        private string ParseResponseLine(string line)
        {
            try
            {
                var json = JsonDocument.Parse(line);
                if (json.RootElement.TryGetProperty("response", out var responseElement))
                {
                    return responseElement.GetString() ?? string.Empty;
                }
            }
            catch (JsonException ex)
            {
                _logger.Warning($"Failed to parse response line: {ex.Message}");
            }
            return string.Empty;
        }

        /// <summary>
        /// Sends code for explanation.
        /// </summary>
        public IAsyncEnumerable<string> ExplainCodeAsync(
            string code,
            string language,
            string explanationStyle,
            CancellationToken cancellationToken = default)
        {
            var prompt = $"Explain the following {language} code ({explanationStyle}):\n\n{code}";
            return SendMessageAsync(prompt, "CodeAnalyzer", "default", cancellationToken);
        }

        /// <summary>
        /// Sends code for test generation.
        /// </summary>
        public IAsyncEnumerable<string> GenerateTestsAsync(
            string code,
            string language,
            CancellationToken cancellationToken = default)
        {
            var prompt = $"Generate unit tests for the following {language} code:\n\n{code}";
            return SendMessageAsync(prompt, "TestGenerator", "default", cancellationToken);
        }

        /// <summary>
        /// Sends code for optimization suggestions.
        /// </summary>
        public IAsyncEnumerable<string> OptimizeCodeAsync(
            string code,
            string language,
            CancellationToken cancellationToken = default)
        {
            var prompt = $"Suggest optimizations for the following {language} code:\n\n{code}";
            return SendMessageAsync(prompt, "CodeOptimizer", "default", cancellationToken);
        }

        /// <summary>
        /// Gets available models from the server.
        /// </summary>
        public async Task<List<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
        {
            var models = new List<string>();
            try
            {
                var baseUrl = await ResolveServerUrlAsync(cancellationToken);
                var modelsUrl = $"{baseUrl}/v1/models";
                using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
                AddApiKeyIfConfigured(request, _cachedResolution);
                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warning($"Failed to get models: {response.StatusCode}");
                    return models;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.TryGetProperty("data", out var dataElement) &&
                    dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idElement))
                        {
                            models.Add(idElement.GetString() ?? string.Empty);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error getting models: {ex.Message}");
            }

            return models;
        }

        /// <summary>
        /// Gets the active inference provider ("ollama" or "lmstudio"), for display purposes
        /// only — provider selection is entirely server-side (config.json), not something this
        /// extension configures. Returns null if the server is unreachable or the field is
        /// absent (older server builds predate this field).
        /// </summary>
        public async Task<string?> GetProviderAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var baseUrl = await ResolveServerUrlAsync(cancellationToken);
                var infoUrl = $"{baseUrl}/api/system/info";
                using var request = new HttpRequestMessage(HttpMethod.Get, infoUrl);
                AddApiKeyIfConfigured(request, _cachedResolution);
                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(json);

                // Server serializes with default (PascalCase) property naming — no camelCase
                // policy configured — unlike /v1/models's hand-built lowercase anonymous object.
                if (document.RootElement.TryGetProperty("Configuration", out var configuration) &&
                    configuration.TryGetProperty("Global", out var global) &&
                    global.TryGetProperty("Provider", out var provider))
                {
                    return provider.GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to get provider: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Adds API key to the request if configured, respecting loopback addresses.
        /// Uses resolved connection info if available, otherwise falls back to settings.
        /// </summary>
        private void AddApiKeyIfConfigured(HttpRequestMessage request, ResolvedServerConnection? resolvedConnection = null)
        {
            string? apiKey = null;

            if (resolvedConnection != null)
            {
                // Use the API key from the resolved connection
                apiKey = resolvedConnection.IsLoopback ? null : resolvedConnection.ApiKey;
            }
            else
            {
                // Fallback to direct settings (for backward compatibility)
                apiKey = _settings.ApiKey;
            }

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
            }
        }
    }
}
