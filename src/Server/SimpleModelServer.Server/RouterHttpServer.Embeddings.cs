using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Hardware;
using SimpleModelServer.Mcp;
using SimpleModelServer.Providers.Ollama;
using SimpleModelServer.Server.Interfaces;
using SimpleModelServer.Server.Infrastructure.Networking;
using SimpleModelServer.Server.Configuration;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server;

internal sealed partial class RouterHttpServer
{
	/// <summary>
	///     Handle Ollama-native POST /api/embed via the configured generation backend, so it works
	///     the same under LM Studio as it does under Ollama (previously this proxied the request
	///     body verbatim straight to Ollama's own HTTP router, which only ever worked for Ollama).
	/// </summary>
	private async Task HandleEmbedAsync(HttpContext context, CancellationToken ct = default)
	{
		string body = await ReadBodyAsync(context.Request);
		JsonNode? json = JsonNode.Parse(body);
		JsonNode? input = json?["input"];
		string model = json?["model"]?.GetValue<string>() ?? generation.EmbedModelName;

		JsonArray? embeddings = await generation.GetEmbeddingsAsync(model, input, ct);
		if (embeddings is null)
		{
			await WriteJsonAsync(context, 502, new { error = "Embed model unavailable" });
			return;
		}

		await WriteJsonAsync(context, 200, new { model, embeddings });
	}

	/// <summary>
	///     Handle OpenAI-compatible /v1/embeddings via the configured generation backend (Ollama's
	///     /api/embed or LM Studio's /v1/embeddings — see <see cref="SimpleModelPairGenerationService.GetEmbeddingsAsync" />).
	/// </summary>
	private async Task ProxyEmbeddingsAsync(HttpContext context, CancellationToken ct = default)
	{
		string body = await ReadBodyAsync(context.Request);
		JsonNode? json = JsonNode.Parse(body);
		JsonNode? input = json?["input"];
		string model = json?["model"]?.GetValue<string>() ?? generation.EmbedModelName;

		JsonArray? embeddings = await generation.GetEmbeddingsAsync(model, input, ct);
		if (embeddings is null)
		{
			await WriteJsonAsync(context, 502, new { error = "Embed model unavailable" });
			return;
		}

		JsonArray dataArray = [];
		for (int i = 0; i < embeddings.Count; i++)
		{
			dataArray.Add(new JsonObject
			{
				["object"] = "embedding",
				["embedding"] = embeddings[i]?.DeepClone(),
				["index"] = i
			});
		}

		await WriteJsonAsync(context, 200, new
		{
			@object = "list",
			data = dataArray,
			model
		});
	}

}
