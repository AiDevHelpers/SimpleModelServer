using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Providers.Common.Generation;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Providers.LMStudio;

/// <summary>
///     Parses LM Studio's OpenAI-compatible SSE stream (<c>data: {...}\n\n</c>, terminated by
///     <c>data: [DONE]</c>) into <see cref="TokenResult" />s. Mirrors
///     <see cref="SimpleModelServer.Providers.Common.Utilities.OllamaHelpers.ReadTokenAsync" />'s
///     timeout-poll pattern (a genuinely-cancelling <see cref="CancellationTokenSource" />, not a
///     <c>Task.WhenAny</c> race, so an abandoned read never leaves the reader "in use").
/// </summary>
internal sealed class LMStudioGenerationHandle(HttpResponseMessage response, StreamReader reader) : IGenerationHandle
{
	public async Task<TokenResult> ReadTokenAsync(TimeSpan timeout)
	{
		try
		{
			string? line;
			try
			{
				using CancellationTokenSource cts = new(timeout);
				line = await reader.ReadLineAsync(cts.Token);
			}
			catch (OperationCanceledException)
			{
				return new TokenResult("", false);
			}

			if (line is null)
			{
				return new TokenResult("", true);
			}

			if (string.IsNullOrWhiteSpace(line))
			{
				// SSE blank-line event separator — not end of stream, keep polling.
				return new TokenResult("", false);
			}

			if (!line.StartsWith("data:", StringComparison.Ordinal))
			{
				// Some other SSE field (e.g. "event:", "id:") or a comment line — ignore.
				return new TokenResult("", false);
			}

			string payload = line["data:".Length..].Trim();
			if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
			{
				return new TokenResult("", true);
			}

			JsonNode? json = JsonNode.Parse(payload);
			string text = json?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>() ?? "";
			return new TokenResult(text, false);
		}
		catch (Exception ex) when (ex is JsonException or IOException or ObjectDisposedException)
		{
			// Malformed chunk, or the underlying stream faulted/closed mid-read — indistinguishable
			// from "the stream ended" to TokenResult's (Text, Done) shape, so report Done=true to
			// stop the polling loop, same as OllamaHelpers.ReadTokenAsync's equivalent catch.
			return new TokenResult("", true);
		}
	}

	public ValueTask DisposeAsync()
	{
		reader.Dispose();
		response.Dispose();
		return ValueTask.CompletedTask;
	}
}
