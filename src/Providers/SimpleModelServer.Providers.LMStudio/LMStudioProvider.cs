using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SimpleModelServer.Contracts.Dtos.Providers;
using SimpleModelServer.Contracts.Providers;

namespace SimpleModelServer.Providers.LMStudio;

/// <summary>
///     LM Studio provider implementation for OpenAI-compatible API communication.
///     Supports the OpenAI v1 chat/completion API endpoints.
/// </summary>
public class LMStudioProvider : ILMStudioProvider
{
	private readonly HttpClient _httpClient;
	private readonly ProviderConfiguration _config;
	private readonly string _baseUrl;

	public string BaseUrl => _baseUrl;

	public LMStudioProvider(ProviderConfiguration config, HttpClient? httpClient = null)
	{
		_config = config ?? throw new ArgumentNullException(nameof(config));
		_httpClient = httpClient ?? new HttpClient();
		_baseUrl = BuildBaseUrl(config.Host, config.Port);
	}

	public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/models");
			AddApiKeyIfConfigured(request);
			using var response = await _httpClient.SendAsync(request, ct);
			return response.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	public async Task<JsonArray?> GetModelsAsync(CancellationToken ct = default)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/models");
			AddApiKeyIfConfigured(request);
			using var response = await _httpClient.SendAsync(request, ct);

			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			string json = await response.Content.ReadAsStringAsync(ct);
			using JsonDocument doc = JsonDocument.Parse(json);

			if (doc.RootElement.TryGetProperty("data", out var dataElement) &&
				dataElement.ValueKind == System.Text.Json.JsonValueKind.Array)
			{
				return JsonNode.Parse(dataElement.GetRawText()) as JsonArray;
			}

			return null;
		}
		catch
		{
			return null;
		}
	}

	public async Task<HttpResponseMessage> PostChatCompletionAsync(
		JsonObject request,
		CancellationToken ct = default,
		HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
	{
		using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions")
		{
			Content = new StringContent(
				request.ToJsonString(),
				Encoding.UTF8,
				"application/json")
		};

		AddApiKeyIfConfigured(httpRequest);
		return await _httpClient.SendAsync(httpRequest, completionOption, ct);
	}

	public async Task<HttpResponseMessage> PostCompletionAsync(JsonObject request, CancellationToken ct = default)
	{
		using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/completions")
		{
			Content = new StringContent(
				request.ToJsonString(),
				Encoding.UTF8,
				"application/json")
		};

		AddApiKeyIfConfigured(httpRequest);
		return await _httpClient.SendAsync(httpRequest, ct);
	}

	public async Task<HttpResponseMessage> PostEmbeddingsAsync(JsonObject request, CancellationToken ct = default)
	{
		using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/embeddings")
		{
			Content = new StringContent(
				request.ToJsonString(),
				Encoding.UTF8,
				"application/json")
		};

		AddApiKeyIfConfigured(httpRequest);
		return await _httpClient.SendAsync(httpRequest, ct);
	}

	private void AddApiKeyIfConfigured(HttpRequestMessage request)
	{
		if (!string.IsNullOrWhiteSpace(_config.ApiKey))
		{
			request.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");
		}
	}

	private static string BuildBaseUrl(string host, int port)
	{
		if (string.IsNullOrWhiteSpace(host))
		{
			host = "127.0.0.1";
		}

		return $"http://{host}:{port}";
	}
}
