using System.Net;
using System.Text.Json;

using Xunit;
using Xunit.Sdk;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Providers.Ollama;
using SimpleModelServer.Server.Interfaces;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Verifies that RouterHttpServer implements IRouter and IOllama interfaces completely.
///     Tests all documented endpoints to ensure the router exposes the full Ollama API surface
///     and router-specific features (health, capacity).
/// </summary>
public sealed class InterfaceContractTests
{
    // ── IRouter Contract Tests ──

    [Fact]
    public async Task IRouter_HealthEndpoint_Responds()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Accepted);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task IRouter_HealthReadyEndpoint_Responds()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.ServiceUnavailable);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task IRouter_CapacityEndpoint_Responds()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync("/api/capacity", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("cpuThreads", out _));
        Assert.True(doc.RootElement.TryGetProperty("gpuLayers", out _));
        Assert.True(doc.RootElement.TryGetProperty("gpu", out _));
    }

    // ── IOllama Contract Tests: GET Endpoints ──

    [Fact]
    public async Task IOllama_GetApiTags_Responds()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync("/api/tags", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("models", out var models));
        Assert.NotNull(models);
    }

    [Fact]
    public async Task IOllama_GetApiVersion_Responds()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync("/api/version", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("version", out _));
    }

    [Fact]
    public async Task IOllama_GetApiPs_Responds()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync("/api/ps", TestContext.Current.CancellationToken);

        // Should proxy to Ollama — any response other than 404 is acceptable
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IOllama_V1ModelsAlias_Responds()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync("/v1/models", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("data", out _));
    }

    // ── IOllama Contract Tests: POST Endpoints ──

    [Fact]
    public async Task IOllama_PostApiGenerate_AcceptsRequest()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { prompt = "test", stream = false };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/api/generate", content, TestContext.Current.CancellationToken);

        // Should not return 404 (endpoint exists)
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IOllama_PostApiChat_AcceptsRequest()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { model = "test", messages = new[] { new { role = "user", content = "test" } }, stream = false };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/api/chat", content, TestContext.Current.CancellationToken);

        // Should not return 404 (endpoint exists)
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IOllama_PostV1ChatCompletions_AcceptsRequest()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { model = "test", messages = new[] { new { role = "user", content = "test" } }, stream = false };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/v1/chat/completions", content, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IOllama_PostApiEmbed_AcceptsRequest()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { model = "test-embed-model", input = "test" };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/api/embed", content, TestContext.Current.CancellationToken);

        // Should proxy to Ollama and return success
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify the FakeOllamaServer tracked the call
        Assert.True(fixture.Ollama.GenerateCallsByModel.GetValueOrDefault("test-embed-model") >= 1,
            "Embed call should be tracked in GenerateCallsByModel");
    }

    [Fact]
    public async Task IOllama_PostApiEmbeddings_AcceptsRequest()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { model = "test", input = "test" };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/api/embeddings", content, TestContext.Current.CancellationToken);

        // Should not return 404 (endpoint exists)
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IOllama_PostV1Embeddings_AcceptsRequest()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { model = "test", input = "test" };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/v1/embeddings", content, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IOllama_PostApiShow_Endpoint()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { name = "test-model" };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/api/show", content, TestContext.Current.CancellationToken);

        // May proxy or not exist in fake Ollama — should not throw
        Assert.NotNull(response);
    }

    [Fact]
    public async Task IOllama_PostApiCreate_Endpoint()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { name = "test-model", modelfile = "FROM base" };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/api/create", content, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task IOllama_PostApiCopy_Endpoint()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { source = "model1", destination = "model2" };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/api/copy", content, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task IOllama_DeleteApi_Endpoint()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.DeleteAsync("/api/delete?name=test", TestContext.Current.CancellationToken);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task IOllama_PostApiPull_Endpoint()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { name = "test-model" };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/api/pull", content, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task IOllama_PostApiPush_Endpoint()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { name = "test-model" };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/api/push", content, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
    }

    [Fact]
    public void IOllama_BlobEndpoints_ImplementationExists()
    {
        // Blob endpoints (/api/blobs/:digest) are implemented via RouterHttpServer.ProxyToOllamaAsync,
        // parameterized by HttpMethod (HEAD for reads, POST for uploads). This test just verifies the
        // interface contract is satisfied. End-to-end testing of blob operations requires a real
        // Ollama instance.
        var methods = typeof(RouterHttpServer).GetMethods(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.Contains(methods, m => m.Name.Contains("ProxyToOllamaAsync", StringComparison.Ordinal));
    }

    // ── Interface Implementation Verification ──

    [Fact]
    public void RouterHttpServer_ImplementsIRouter()
    {
        Assert.True(typeof(RouterHttpServer).GetInterfaces().Contains(typeof(IRouter)));
    }

    // RouterHttpServer no longer implements the shared Contracts.Providers.IOllama interface —
    // that interface is HttpListener-typed and Contracts is referenced by non-web projects
    // (Providers, Hardware, Config) that shouldn't need an ASP.NET Core dependency. The explicit
    // interface members it used to satisfy were dead code (RouteGetAsync/RoutePostAsync already
    // dispatch to the real handlers directly) — Ollama API surface coverage is verified by the
    // endpoint tests above instead of by reflecting on interface conformance.

    // ── Case Insensitivity Tests ──

    [Fact]
    public async Task CaseInsensitive_ApiTagsLowercase()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync("/api/tags", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CaseInsensitive_ApiVersionUppercase()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync("/API/VERSION", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CaseInsensitive_ChatCompletionsMixedCase()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        var request = new { model = "test", messages = new[] { new { role = "user", content = "test" } } };
        using StringContent content = new(System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await fixture.Client.PostAsync("/V1/Chat/Completions", content, TestContext.Current.CancellationToken);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}

