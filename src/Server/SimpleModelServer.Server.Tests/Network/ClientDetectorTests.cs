using System.Text;
using System.Text.Json;

using Xunit;
using Xunit.Sdk;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Verifies that DraftModel's router correctly handles and identifies TokenSaver clients
///     based on User-Agent headers. TokenSaver sends requests with User-Agent like "TokenSaver/1.0.33"
///     and should be detected as an Ollama-compatible client, receiving Ollama format responses.
/// </summary>
public sealed class ClientDetectorTests : IAsyncLifetime
{
    private RouterTestFixture? fixture;

    public async ValueTask InitializeAsync()
    {
        fixture = await RouterTestFixture.CreateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (fixture != null)
        {
            await fixture.DisposeAsync();
        }
    }

    private static StringContent JsonBody(string json) =>
        new(json, Encoding.UTF8, "application/json");

    [Theory]
    [InlineData("TokenSaver/1.0.33")]
    [InlineData("TokenSaver/1.0.0")]
    [InlineData("tokensaver/2.0")]
    [InlineData("TOKENSAVER/1.5")]
    public async Task ApiGenerate_TokenSaverUserAgent_ReturnsOllamaFormat(string userAgent)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/generate")
        {
            Content = JsonBody("""{"prompt":"code test","stream":false}""")
        };
        request.Headers.Add("User-Agent", userAgent);
        fixture!.Ollama.GenerateResponseText = "test response";

        using var response = await fixture!.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        // Ollama format includes "model" and "response" fields
        Assert.NotNull(doc.RootElement.GetProperty("model").GetString());
        Assert.NotNull(doc.RootElement.GetProperty("response").GetString());
        Assert.True(doc.RootElement.GetProperty("done").GetBoolean());
    }

    [Theory]
    [InlineData("ollama/0.1")]
    [InlineData("Ollama CLI")]
    [InlineData("localpilot/1.0")]
    public async Task ApiGenerate_KnownOllamaClients_ReturnsOllamaFormat(string userAgent)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/generate")
        {
            Content = JsonBody("""{"prompt":"code test","stream":false}""")
        };
        request.Headers.Add("User-Agent", userAgent);
        fixture!.Ollama.GenerateResponseText = "test response";

        using var response = await fixture!.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);
        // Ollama format response
        Assert.NotNull(doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ApiGenerate_NoUserAgent_DefaultsToOllamaFormat()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/generate")
        {
            Content = JsonBody("""{"prompt":"code test","stream":false}""")
        };
        // Deliberately don't set User-Agent
        fixture!.Ollama.GenerateResponseText = "test response";

        using var response = await fixture!.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);
        // Should get Ollama format response (default)
        Assert.NotNull(doc.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("curl/7.68.0")]
    [InlineData("Python-Requests/2.28.0")]
    public async Task ChatCompletions_UnknownUserAgent_ReturnsOpenAiFormat(string userAgent)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1/chat/completions")
        {
            Content = JsonBody("""{"messages":[{"role":"user","content":"code this"}],"stream":false}""")
        };
        request.Headers.Add("User-Agent", userAgent);
        fixture!.Ollama.GenerateResponseText = "test response";

        using var response = await fixture!.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        // OpenAI format includes "object" field set to "chat.completion"
        Assert.Equal("chat.completion", doc.RootElement.GetProperty("object").GetString());
    }

    [Fact]
    public async Task ApiGenerate_XClientFormatHeaderOverridesUserAgent()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/generate")
        {
            Content = JsonBody("""{"prompt":"code test","stream":false}""")
        };
        request.Headers.Add("User-Agent", "unknown-client");
        request.Headers.Add("X-Client-Format", "ollama");
        fixture!.Ollama.GenerateResponseText = "test response";

        using var response = await fixture!.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        // Should get Ollama format due to X-Client-Format header override
        Assert.NotNull(doc.RootElement.GetProperty("model").GetString());
    }
}
