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
	private async Task HandleOllamaGenerateAsync(HttpContext context, CancellationToken ct = default)
	{
		if (await CheckModelWarmupAsync(context))
		{
			return;
		}

		await WaitForModelsReadyAsync(ct);

		string body = await ReadBodyAsync(context.Request);
		JsonNode? json = JsonNode.Parse(body);
		string prompt = json?["prompt"]?.GetValue<string>() ?? "";


		// Unknown agent types must fall back to the configured default agent type,
		// not the "current agent" (which caused 202 Accepted responses in tests).
		string fallbackAgentType = config.DefaultAgentType;
		string agentType = agentTypeResolver.Resolve(context.Request, json, prompt, fallbackAgentType);


		bool stream = json?["stream"]?.GetValue<bool>() ?? true;

		// ── Research path ──
		if (string.Equals(agentType, "research", StringComparison.OrdinalIgnoreCase))
		{
			bool streamedDirectly = false;

			string responseText = await generation.ResolveResearchResponseAsync(prompt, async result =>
			{
				// Ollama-native streaming has a fast path the OpenAI-compatible handler doesn't
				// use: when web results are found, stream the draft-pair response to the
				// enriched prompt directly instead of waiting for the full text.
				if (!stream)
				{
					return false;
				}

				string enriched = SimpleModelPairGenerationService.BuildResearchPromptFor(result);
				context.Response.ContentType = "application/x-ndjson";
				context.Response.StatusCode = 200;
				await generation.StreamDraftPairAsync(enriched, "research", async token =>
					await WriteRawAsync(context.Response,
						JsonSerializer.Serialize(new { response = token, done = false }) + "\n"), ct);
				await WriteRawAsync(context.Response,
					JsonSerializer.Serialize(new { response = "", done = true }) + "\n");
				streamedDirectly = true;
				return true;
			}, ct);

			if (streamedDirectly)
			{
				return;
			}

			if (stream)
			{
				context.Response.ContentType = "application/x-ndjson";
				context.Response.StatusCode = 200;
				await WriteRawAsync(context.Response,
					JsonSerializer.Serialize(new { response = responseText, done = false }) + "\n");
				await WriteRawAsync(context.Response,
					JsonSerializer.Serialize(new { response = "", done = true }) + "\n");
			}
			else
			{
				await WriteJsonAsync(context, 200,
					new { model = RouterModelName, response = responseText, done = true });
			}

			return;
		}

		if (!stream)
		{
			string final = await generation.GetDraftPairResponseAsync([new ChatMessage("user", prompt)], agentType, ct);
			await WriteJsonAsync(context, 200, new { model = RouterModelName, response = final, done = true });
			return;
		}

		context.Response.ContentType = "application/x-ndjson";
		context.Response.StatusCode = 200;

		await generation.StreamDraftPairAsync(prompt, agentType,
			async token =>
			{
				await WriteRawAsync(context.Response,
					JsonSerializer.Serialize(new { response = token, done = false }) + "\n");
			}, ct);

		await WriteRawAsync(context.Response, JsonSerializer.Serialize(new { response = "", done = true }) + "\n");
	}

	private async Task HandleOpenAiCompletionAsync(HttpContext context, CancellationToken ct = default)
	{
		if (await CheckModelWarmupAsync(context))
		{
			return;
		}

		await WaitForModelsReadyAsync(ct);

		string body = await ReadBodyAsync(context.Request);
		JsonNode? json = JsonNode.Parse(body);
		string prompt = json?["prompt"]?.GetValue<string>() ?? "";


		string fallbackAgentType = defaultAgentType;
		string agentType = agentTypeResolver.Resolve(context.Request, json, prompt, fallbackAgentType);


		string final = await generation.GetDraftPairResponseAsync([new ChatMessage("user", prompt)], agentType, ct);
		await WriteJsonAsync(context, 200, new
		{
			id = $"cmpl-{Guid.NewGuid()}",
			@object = "text_completion",
			choices = new[] { new { index = 0, text = final, finish_reason = "stop" } }
		});
	}

	// This is the single choke point every POST handler funnels through, so the shared
	// ReadBodyAsync's optional debug logging catches every body exactly once without re-reading
	// the (once-only) input stream or threading the text through handler signatures.
}
