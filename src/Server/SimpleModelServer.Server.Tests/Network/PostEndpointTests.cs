using System.Net;
using System.Text;
using System.Text.Json;

using Xunit;
using Xunit.Sdk;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers every POST endpoint documented in docs/router-contract.md, using the "code" agent
///     (avoids the research classification path, which calls out to the fake Ollama a second time
///     and is covered separately) unless the test is specifically about routing/streaming shape.
/// </summary>
public sealed class PostEndpointTests
{
    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task ApiGenerate_NonStreaming_ReturnsFullResponse()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.GenerateResponseText = "the answer";

        using HttpResponseMessage response = await fixture.Client.PostAsync(
            new Uri("/api/generate", UriKind.Relative), JsonBody("""{"prompt":"say code hello","stream":false}"""), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("TestRouterModel", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal("the answer", doc.RootElement.GetProperty("response").GetString());
        Assert.True(doc.RootElement.GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task ApiGenerate_Streaming_ReturnsNdjsonWithFinalDoneLine()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.GenerateResponseText = "streamed-token";

        using HttpResponseMessage response = await fixture.Client.PostAsync(
            new Uri("/api/generate", UriKind.Relative), JsonBody("""{"prompt":"code this","stream":true}"""), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        string[] lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 1);

        JsonDocument lastLine = JsonDocument.Parse(lines[^1]);
        Assert.True(lastLine.RootElement.GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task ChatCompletions_NonStreaming_ReturnsOpenAiShape()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.GenerateResponseText = "chat response";

        using HttpResponseMessage response = await fixture.Client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            JsonBody("""{"messages":[{"role":"user","content":"code this please"}],"stream":false}"""), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("chat.completion", doc.RootElement.GetProperty("object").GetString());
        JsonElement choice = doc.RootElement.GetProperty("choices")[0];
        Assert.Equal("assistant", choice.GetProperty("message").GetProperty("role").GetString());
        Assert.Equal("stop", choice.GetProperty("finish_reason").GetString());
    }

    [Fact]
    public async Task ChatCompletionsAlias_BehavesTheSameAsV1()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.PostAsync(
            new Uri("/chat/completions", UriKind.Relative),
            JsonBody("""{"messages":[{"role":"user","content":"code this please"}],"stream":false}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChatCompletions_Streaming_ReturnsSseWithDoneMarker()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            JsonBody("""{"messages":[{"role":"user","content":"code this please"}],"stream":true}"""), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("data: ", body, StringComparison.Ordinal);
        Assert.Contains("[DONE]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Completions_ReturnsTextCompletionShape()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.GenerateResponseText = "legacy completion text";

        using HttpResponseMessage response = await fixture.Client.PostAsync(
            new Uri("/v1/completions", UriKind.Relative), JsonBody("""{"prompt":"code this"}"""), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text_completion", doc.RootElement.GetProperty("object").GetString());
        Assert.Equal("legacy completion text", doc.RootElement.GetProperty("choices")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task CompletionsAlias_BehavesTheSameAsV1()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.PostAsync(
            new Uri("/completions", UriKind.Relative), JsonBody("""{"prompt":"code this"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiEmbed_ProxiesRawOllamaResponse()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.PostAsync(
            new Uri("/api/embed", UriKind.Relative), JsonBody("""{"model":"nomic-embed-text","input":"hello"}"""), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(doc.RootElement.TryGetProperty("embeddings", out _));
    }

    [Theory]
    [InlineData("/v1/embeddings")]
    [InlineData("/embeddings")]
    public async Task EmbeddingsEndpoints_TranslateToOpenAiShape(string path)
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.PostAsync(
            new Uri(path, UriKind.Relative), JsonBody("""{"model":"nomic-embed-text","input":"hello"}"""), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("list", doc.RootElement.GetProperty("object").GetString());
        JsonElement embedding = doc.RootElement.GetProperty("data")[0];
        Assert.Equal("embedding", embedding.GetProperty("object").GetString());
        Assert.Equal(0, embedding.GetProperty("index").GetInt32());
        Assert.Equal("nomic-embed-text", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task UnknownPostPath_Returns404()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.PostAsync(new Uri("/nonexistent", UriKind.Relative), JsonBody("{}"), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not found", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ApiGenerate_AgentTypeHeader_IsHonored()
    {
        // "code" is the only configured agent in the fixture; passing X-Agent-Type: code should
        // resolve without falling back to keyword-scanning the prompt, and still produce a normal
        // response — this is the header-based branch of AgentTypeResolver's priority order.
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.GenerateResponseText = "ok";

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/generate")
        {
            Content = JsonBody("""{"prompt":"do something ambiguous","stream":false}""")
        };
        request.Headers.Add("X-Agent-Type", "code");

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", doc.RootElement.GetProperty("response").GetString());
    }

    [Fact]
    public async Task ApiChat_NonStreaming_ReturnsOpenAiCompatibleResponse()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.GenerateResponseText = "chat response text";

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/chat")
        {
            Content = JsonBody("""{"model":"code","messages":[{"role":"user","content":"hello"}],"stream":false}""")
        };
        request.Headers.Add("X-Client-Format", "openai");

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("chat.completion", doc.RootElement.GetProperty("object").GetString());
        JsonElement choice = doc.RootElement.GetProperty("choices")[0];
        Assert.Equal("assistant", choice.GetProperty("message").GetProperty("role").GetString());
        Assert.Equal("chat response text", choice.GetProperty("message").GetProperty("content").GetString());
        Assert.Equal("stop", choice.GetProperty("finish_reason").GetString());
    }

    [Fact]
    public async Task ApiChat_Streaming_ReturnsSseWithDoneMarker()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.GenerateResponseText = "streamed-chat-token";

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/chat")
        {
            Content = JsonBody("""{"model":"code","messages":[{"role":"user","content":"hello"}],"stream":true}""")
        };
        request.Headers.Add("X-Client-Format", "openai");

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("data: ", body, StringComparison.Ordinal);
        Assert.Contains("[DONE]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiChat_OllamaClient_ReturnsOllamaNativeFormat()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.GenerateResponseText = "ollama response";

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/chat")
        {
            Content = JsonBody("""{"model":"code","messages":[{"role":"user","content":"hello"}],"stream":false}""")
        };
        request.Headers.Add("User-Agent", "ollama/0.1.0");

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("TestRouterModel", doc.RootElement.GetProperty("model").GetString());
        JsonElement message = doc.RootElement.GetProperty("message");
        Assert.Equal("assistant", message.GetProperty("role").GetString());
        Assert.Equal("ollama response", message.GetProperty("content").GetString());
        Assert.True(doc.RootElement.GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task ApiChat_OllamaClient_Streaming_ReturnsNdjson()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.GenerateResponseText = "streamed-response";

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/chat")
        {
            Content = JsonBody("""{"model":"code","messages":[{"role":"user","content":"hello"}],"stream":true}""")
        };
        request.Headers.Add("User-Agent", "ollama/0.1.0");

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        string[] lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 1);

        JsonDocument lastLine = JsonDocument.Parse(lines[^1]);
        Assert.True(lastLine.RootElement.GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task ApiChat_ExplicitOllamaHeader_ReturnsOllamaNativeFormat()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.GenerateResponseText = "explicit ollama format";

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/chat")
        {
            Content = JsonBody("""{"model":"code","messages":[{"role":"user","content":"hello"}],"stream":false}""")
        };
        request.Headers.Add("X-Client-Format", "ollama");

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(doc.RootElement.TryGetProperty("message", out _));
        Assert.True(doc.RootElement.TryGetProperty("done", out _));
    }

}
