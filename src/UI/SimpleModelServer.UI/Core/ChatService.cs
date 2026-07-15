using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleModelServer.UI.Core
{
    /// <summary>
    /// Service for communicating with Simple Model Server API.
    /// Handles message sending, response streaming, and model operations.
    /// </summary>
    public sealed class ChatService : IDisposable
    {
        private readonly SettingsManager _settings;
        private readonly HttpClient _httpClient;
        private readonly DiagnosticLogger _logger;
        private readonly bool _ownsHttpClient;

        public ChatService(SettingsManager settings, HttpClient? httpClient = null, DiagnosticLogger? logger = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _httpClient = httpClient ?? new HttpClient();
            _ownsHttpClient = httpClient == null;
            _logger = logger ?? new DiagnosticLogger("ChatService");
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
        /// Tests the connection to the Simple Model Server.
        /// </summary>
        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var healthUrl = $"{_settings.GetServerUrl()}/health";
                using var request = new HttpRequestMessage(HttpMethod.Get, healthUrl);
                AddApiKeyIfConfigured(request);
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
            var url = _settings.GetChatEndpoint();

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

            AddApiKeyIfConfigured(httpRequest);

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
                var modelsUrl = $"{_settings.GetServerUrl()}/v1/models";
                using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
                AddApiKeyIfConfigured(request);
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

        private void AddApiKeyIfConfigured(HttpRequestMessage request)
        {
            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {_settings.ApiKey}");
            }
        }
    }
}
