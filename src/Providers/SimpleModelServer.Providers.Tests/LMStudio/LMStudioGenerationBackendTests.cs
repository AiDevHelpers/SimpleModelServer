using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimpleModelServer.Contracts.Dtos.Providers;
using SimpleModelServer.Providers.Common.Generation;
using Xunit;

namespace SimpleModelServer.Providers.LMStudio.Tests;

/// <summary>
///     Tests for <see cref="LMStudioGenerationBackend" />/<see cref="LMStudioGenerationHandle" />
///     against canned HTTP responses — no live LM Studio instance required.
/// </summary>
public class LMStudioGenerationBackendTests
{
	private static LMStudioGenerationBackend CreateBackend(string responseBody, string contentType, HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		var config = new ProviderConfiguration { Host = "localhost", Port = 1234 };
		var handler = new CannedResponseHandler(responseBody, contentType, statusCode);
		var httpClient = new HttpClient(handler);
		var provider = new LMStudioProvider(config, httpClient);
		return new LMStudioGenerationBackend(provider);
	}

	[Fact]
	public async Task StartStreamAsync_NormalMultiChunkStream_YieldsEachDeltaThenDone()
	{
		// Arrange — a typical multi-chunk SSE stream: role-only first chunk, two content chunks,
		// a finish_reason chunk with no content, then [DONE].
		string sse = string.Join("\n\n",
			"""data: {"choices":[{"delta":{"role":"assistant"}}]}""",
			"""data: {"choices":[{"delta":{"content":"Hello"}}]}""",
			"""data: {"choices":[{"delta":{"content":" world"}}]}""",
			"""data: {"choices":[{"delta":{},"finish_reason":"stop"}]}""",
			"data: [DONE]") + "\n\n";

		LMStudioGenerationBackend backend = CreateBackend(sse, "text/event-stream");

		// Act
		await using IGenerationHandle handle = await backend.StartStreamAsync("test-model", "hi", GenerationOptions.None, CancellationToken.None);

		string collected = "";
		bool done = false;
		while (!done)
		{
			var token = await handle.ReadTokenAsync(TimeSpan.FromSeconds(5));
			done = token.Done;
			collected += token.Text;
		}

		// Assert
		Assert.Equal("Hello world", collected);
	}

	[Fact]
	public async Task StartStreamAsync_ImmediateDone_YieldsNoTokens()
	{
		// Arrange
		LMStudioGenerationBackend backend = CreateBackend("data: [DONE]\n\n", "text/event-stream");

		// Act
		await using IGenerationHandle handle = await backend.StartStreamAsync("test-model", "hi", GenerationOptions.None, CancellationToken.None);
		var token = await handle.ReadTokenAsync(TimeSpan.FromSeconds(5));

		// Assert
		Assert.True(token.Done);
		Assert.Equal("", token.Text);
	}

	[Fact]
	public async Task StartStreamAsync_ChunkWithNoDeltaContent_YieldsEmptyTextNotDone()
	{
		// Arrange — a chunk that only carries a role/finish_reason, no "content" key at all.
		LMStudioGenerationBackend backend = CreateBackend(
			"""data: {"choices":[{"delta":{"role":"assistant"}}]}""" + "\n\n", "text/event-stream");

		// Act
		await using IGenerationHandle handle = await backend.StartStreamAsync("test-model", "hi", GenerationOptions.None, CancellationToken.None);
		var token = await handle.ReadTokenAsync(TimeSpan.FromSeconds(5));

		// Assert
		Assert.False(token.Done);
		Assert.Equal("", token.Text);
	}

	[Fact]
	public async Task GenerateAsync_NonStreamResponse_ReturnsMessageContent()
	{
		// Arrange
		string body = """{"choices":[{"message":{"role":"assistant","content":"the answer"}}]}""";
		LMStudioGenerationBackend backend = CreateBackend(body, "application/json");

		// Act
		string result = await backend.GenerateAsync("test-model", "hi", GenerationOptions.None, CancellationToken.None);

		// Assert
		Assert.Equal("the answer", result);
	}

	[Fact]
	public async Task GenerateAsync_ErrorStatusCode_ReturnsEmptyString()
	{
		// Arrange — an error body arriving as a plain JSON object, not an SSE stream.
		LMStudioGenerationBackend backend = CreateBackend(
			"""{"error":"model not loaded"}""", "application/json", HttpStatusCode.BadRequest);

		// Act
		string result = await backend.GenerateAsync("test-model", "hi", GenerationOptions.None, CancellationToken.None);

		// Assert
		Assert.Equal("", result);
	}

	[Fact]
	public async Task GenerateEmbeddingsAsync_NormalResponse_ReturnsEmbeddingsInIndexOrder()
	{
		// Arrange — OpenAI's embeddings response format, deliberately out of index order to
		// confirm the backend sorts by "index" rather than trusting array order.
		string body = """
            {
                "data": [
                    { "embedding": [0.4, 0.5], "index": 1 },
                    { "embedding": [0.1, 0.2], "index": 0 }
                ],
                "model": "test-embed-model"
            }
            """;
		LMStudioGenerationBackend backend = CreateBackend(body, "application/json");

		// Act
		System.Text.Json.Nodes.JsonArray? result = await backend.GenerateEmbeddingsAsync(
			"test-embed-model", System.Text.Json.Nodes.JsonValue.Create("hello"), CancellationToken.None);

		// Assert
		Assert.NotNull(result);
		Assert.Equal(2, result.Count);
		Assert.Equal(0.1, result[0]?[0]?.GetValue<double>());
		Assert.Equal(0.4, result[1]?[0]?.GetValue<double>());
	}

	[Fact]
	public async Task GenerateEmbeddingsAsync_ErrorStatusCode_ReturnsNull()
	{
		// Arrange
		LMStudioGenerationBackend backend = CreateBackend(
			"""{"error":"model not loaded"}""", "application/json", HttpStatusCode.BadRequest);

		// Act
		System.Text.Json.Nodes.JsonArray? result = await backend.GenerateEmbeddingsAsync(
			"test-embed-model", System.Text.Json.Nodes.JsonValue.Create("hello"), CancellationToken.None);

		// Assert
		Assert.Null(result);
	}

	private sealed class CannedResponseHandler(string body, string contentType, HttpStatusCode statusCode) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var response = new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(body, Encoding.UTF8, contentType)
			};
			return Task.FromResult(response);
		}
	}
}
