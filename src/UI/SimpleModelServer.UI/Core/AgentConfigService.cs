using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleModelServer.UI.Core
{
	/// <summary>An agent's editable settings as reported by GET /admin/config.</summary>
	public sealed record AgentPromptInfo(string Name, string SystemPrompt, double? Temperature);

	/// <summary>Result of POST /admin/config for an "agents.&lt;name&gt;.&lt;field&gt;" setting.</summary>
	public sealed record AgentUpdateResult(bool Success, bool SuggestGoldenSet, string Message);

	/// <summary>
	/// Reads and updates agent (code/debug/planning) configuration via the server's
	/// /admin/config endpoint.
	/// </summary>
	public sealed class AgentConfigService : IDisposable
	{
		private readonly SettingsManager _settings;
		private readonly HttpClient _httpClient;
		private readonly DiagnosticLogger _logger;
		private readonly bool _ownsHttpClient;

		public AgentConfigService(SettingsManager settings, HttpClient? httpClient = null, DiagnosticLogger? logger = null)
		{
			_settings = settings ?? throw new ArgumentNullException(nameof(settings));
			_httpClient = httpClient ?? new HttpClient();
			_ownsHttpClient = httpClient == null;
			_logger = logger ?? new DiagnosticLogger("AgentConfigService");
		}

		public void Dispose()
		{
			if (_ownsHttpClient)
			{
				_httpClient.Dispose();
			}
			GC.SuppressFinalize(this);
		}

		/// <summary>Fetches all agents' current settings from the server.</summary>
		public async Task<List<AgentPromptInfo>> GetAgentsAsync(CancellationToken cancellationToken = default)
		{
			var agents = new List<AgentPromptInfo>();
			try
			{
				var url = $"{_settings.GetServerUrl()}/admin/config";
				using var request = new HttpRequestMessage(HttpMethod.Get, url);
				AddApiKeyIfConfigured(request);
				using var response = await _httpClient.SendAsync(request, cancellationToken);

				if (!response.IsSuccessStatusCode)
				{
					_logger.Warning($"Failed to get agent config: {response.StatusCode}");
					return agents;
				}

				var json = await response.Content.ReadAsStringAsync(cancellationToken);
				using var document = JsonDocument.Parse(json);

				if (document.RootElement.TryGetProperty("agents", out var agentsElement) &&
					agentsElement.ValueKind == JsonValueKind.Object)
				{
					foreach (var property in agentsElement.EnumerateObject())
					{
						string systemPrompt = property.Value.TryGetProperty("systemPrompt", out var promptEl)
							? promptEl.GetString() ?? string.Empty
							: string.Empty;
						double? temperature = property.Value.TryGetProperty("temperature", out var tempEl) &&
											   tempEl.ValueKind == JsonValueKind.Number
							? tempEl.GetDouble()
							: null;

						agents.Add(new AgentPromptInfo(property.Name, systemPrompt, temperature));
					}
				}
			}
			catch (Exception ex)
			{
				_logger.Error($"Error getting agent config: {ex.Message}");
			}

			return agents;
		}

		/// <summary>Updates a single agent's system prompt.</summary>
		public Task<AgentUpdateResult> UpdateAgentPromptAsync(
			string agentName,
			string systemPrompt,
			CancellationToken cancellationToken = default)
			=> UpdateAgentSettingAsync(agentName, "systemPrompt", systemPrompt, cancellationToken);

		/// <summary>Updates a single "agents.&lt;name&gt;.&lt;field&gt;" setting.</summary>
		public async Task<AgentUpdateResult> UpdateAgentSettingAsync(
			string agentName,
			string fieldName,
			object? value,
			CancellationToken cancellationToken = default)
		{
			try
			{
				var url = $"{_settings.GetServerUrl()}/admin/config";
				var body = new { setting = $"agents.{agentName}.{fieldName}", value };

				using var request = new HttpRequestMessage(HttpMethod.Post, url)
				{
					Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
				};
				AddApiKeyIfConfigured(request);

				using var response = await _httpClient.SendAsync(request, cancellationToken);
				var json = await response.Content.ReadAsStringAsync(cancellationToken);

				if (!response.IsSuccessStatusCode)
				{
					string errorMessage = ExtractResponseMessage(json) ?? $"Server returned {response.StatusCode}";
					_logger.Warning($"Agent setting update failed: {errorMessage}");
					return new AgentUpdateResult(false, false, errorMessage);
				}

				var (suggestGoldenSet, message) = ExtractUpdateResponse(json);
				return new AgentUpdateResult(true, suggestGoldenSet, message);
			}
			catch (Exception ex)
			{
				_logger.Error($"Error updating agent setting: {ex.Message}");
				return new AgentUpdateResult(false, false, $"Error: {ex.Message}");
			}
		}

		private static string? ExtractResponseMessage(string json)
		{
			try
			{
				using var document = JsonDocument.Parse(json);
				return document.RootElement.TryGetProperty("message", out var el) ? el.GetString() : null;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static (bool SuggestGoldenSet, string Message) ExtractUpdateResponse(string json)
		{
			try
			{
				using var document = JsonDocument.Parse(json);
				bool suggestGoldenSet = document.RootElement.TryGetProperty("suggestGoldenSet", out var gEl) &&
										gEl.ValueKind == JsonValueKind.True;
				string? message = document.RootElement.TryGetProperty("message", out var mEl) ? mEl.GetString() : null;
				return (suggestGoldenSet, message ?? "Updated.");
			}
			catch (JsonException)
			{
				return (false, "Updated.");
			}
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
