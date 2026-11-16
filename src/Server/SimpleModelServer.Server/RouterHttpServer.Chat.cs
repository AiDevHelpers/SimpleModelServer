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
	private async Task HandleOpenAiChatAsync(HttpContext context, CancellationToken ct = default)
	{
		if (await CheckModelWarmupAsync(context))
		{
			return;
		}

		await WaitForModelsReadyAsync(ct);

		string body = await ReadBodyAsync(context.Request);
		JsonNode? json = JsonNode.Parse(body);
		JsonArray messages = json?["messages"]?.AsArray() ?? [];
		bool stream = json?["stream"]?.GetValue<bool>() ?? false;
		string prompt = ollamaHelpers.BuildPrompt(messages);
		string agentType = agentTypeResolver.Resolve(context.Request, json, prompt, currentAgentType());

		// ── Research path ──
		if (string.Equals(agentType, "research", StringComparison.OrdinalIgnoreCase))
		{
			// Unlike the Ollama-native handler, OpenAI-compatible streaming here always waits
			// for the full response text (SSE emits it as a single delta chunk below) rather
			// than streaming the draft-pair tokens live, so no onWebResultsFound hook is needed.
			string responseText = await generation.ResolveResearchResponseAsync(prompt, ct: ct);

			if (stream)
			{
				context.Response.ContentType = "text/event-stream";
				context.Response.StatusCode = 200;
				string payload = JsonSerializer.Serialize(new
				{ choices = new[] { new { delta = new { content = responseText } } } });
				await WriteRawAsync(context.Response, $"data: {payload}\n\n");
				await WriteRawAsync(context.Response, "data: [DONE]\n\n");
			}
			else
			{
				await WriteJsonAsync(context, 200, new
				{
					id = $"chatcmpl-{Guid.NewGuid()}",
					@object = "chat.completion",
					choices = new[]
					{
						new
						{
							index = 0, message = new { role = "assistant", content = responseText },
							finish_reason = "stop"
						}
					}
				});
			}

			return;
		}

		if (stream)
		{
			context.Response.ContentType = "text/event-stream";
			context.Response.StatusCode = 200;
			await generation.StreamDraftPairAsync(prompt, agentType, async token =>
			{
				string payload = JsonSerializer.Serialize(new
				{ choices = new[] { new { delta = new { content = token } } } });
				await WriteRawAsync(context.Response, $"data: {payload}\n\n");
			}, ct);
			await WriteRawAsync(context.Response, "data: [DONE]\n\n");
			return;
		}

		string final2 = await generation.GetDraftPairResponseAsync([new ChatMessage("user", prompt)], agentType, ct);
		await WriteJsonAsync(context, 200, new
		{
			id = $"chatcmpl-{Guid.NewGuid()}",
			@object = "chat.completion",
			choices = new[]
			{
				new { index = 0, message = new { role = "assistant", content = final2 }, finish_reason = "stop" }
			}
		});
	}

	private async Task HandleOllamaChatAsync(HttpContext context, CancellationToken ct = default)
	{
		if (await CheckModelWarmupAsync(context))
		{
			return;
		}

		await WaitForModelsReadyAsync(ct);

		string body = await ReadBodyAsync(context.Request);
		JsonNode? json = JsonNode.Parse(body);
		JsonArray messages = json?["messages"]?.AsArray() ?? [];
		bool stream = json?["stream"]?.GetValue<bool>() ?? false;
		string prompt = ollamaHelpers.BuildPrompt(messages);
		string agentType = agentTypeResolver.Resolve(context.Request, json, prompt, currentAgentType());

		// ── Research path ──
		if (string.Equals(agentType, "research", StringComparison.OrdinalIgnoreCase))
		{
			string responseText = await generation.ResolveResearchResponseAsync(prompt, ct: ct);

			if (stream)
			{
				context.Response.ContentType = "application/x-ndjson";
				context.Response.StatusCode = 200;
				await WriteRawAsync(context.Response,
					JsonSerializer.Serialize(new { message = new { role = "assistant", content = responseText }, done = false }) + "\n");
				await WriteRawAsync(context.Response,
					JsonSerializer.Serialize(new { message = new { role = "assistant", content = "" }, done = true }) + "\n");
			}
			else
			{
				await WriteJsonAsync(context, 200, new
				{
					model = RouterModelName,
					message = new { role = "assistant", content = responseText },
					done = true
				});
			}

			return;
		}

		if (stream)
		{
			context.Response.ContentType = "application/x-ndjson";
			context.Response.StatusCode = 200;
			await generation.StreamDraftPairAsync(prompt, agentType, async token =>
				await WriteRawAsync(context.Response,
					JsonSerializer.Serialize(new { message = new { role = "assistant", content = token }, done = false }) + "\n"), ct);
			await WriteRawAsync(context.Response,
				JsonSerializer.Serialize(new { message = new { role = "assistant", content = "" }, done = true }) + "\n");
			return;
		}

		string final = await generation.GetDraftPairResponseAsync([new ChatMessage("user", prompt)], agentType, ct);
		await WriteJsonAsync(context, 200, new
		{
			model = RouterModelName,
			message = new { role = "assistant", content = final },
			done = true
		});
	}

}
