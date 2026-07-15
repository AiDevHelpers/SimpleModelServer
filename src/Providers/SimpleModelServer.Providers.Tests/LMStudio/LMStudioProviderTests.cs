using System;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SimpleModelServer.Contracts.Dtos.Providers;
using Xunit;

namespace SimpleModelServer.Providers.LMStudio.Tests;

public class LMStudioProviderTests
{
    [Fact]
    public void Constructor_BuildsCorrectBaseUrl()
    {
        var config = new ProviderConfiguration
        {
            Type = SimpleModelServer.Contracts.Providers.Provider.LMStudio,
            Host = "localhost",
            Port = 1234,
        };

        var provider = new LMStudioProvider(config);

        Assert.Equal("http://localhost:1234", provider.BaseUrl);
    }

    [Fact]
    public void Constructor_DefaultsToLocalhost()
    {
        var config = new ProviderConfiguration
        {
            Type = SimpleModelServer.Contracts.Providers.Provider.LMStudio,
            Port = 1234,
        };

        var provider = new LMStudioProvider(config);

        Assert.Equal("http://127.0.0.1:1234", provider.BaseUrl);
    }

    [Fact]
    public void Constructor_SupportsApiKey()
    {
        var config = new ProviderConfiguration
        {
            Type = SimpleModelServer.Contracts.Providers.Provider.LMStudio,
            Host = "localhost",
            Port = 1234,
            ApiKey = "test-key-123",
        };

        var provider = new LMStudioProvider(config);

        Assert.NotNull(provider);
    }

    [Fact]
    public async Task PostChatCompletionAsync_SendsRequest()
    {
        var config = new ProviderConfiguration
        {
            Type = SimpleModelServer.Contracts.Providers.Provider.LMStudio,
            Host = "localhost",
            Port = 1234,
        };

        var mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(mockHandler);

        var provider = new LMStudioProvider(config, httpClient);

        var request = new JsonObject
        {
            ["model"] = "neural-chat",
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = "Hello" }
            }
        };

        using var response = await provider.PostChatCompletionAsync(request);

        Assert.True(mockHandler.LastRequest?.RequestUri?.AbsolutePath.Contains("/v1/chat/completions"));
    }

    [Fact]
    public async Task PostChatCompletionAsync_IncludesApiKeyHeader()
    {
        var config = new ProviderConfiguration
        {
            Type = SimpleModelServer.Contracts.Providers.Provider.LMStudio,
            Host = "localhost",
            Port = 1234,
            ApiKey = "test-api-key",
        };

        var mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(mockHandler);

        var provider = new LMStudioProvider(config, httpClient);

        var request = new JsonObject
        {
            ["model"] = "neural-chat",
            ["messages"] = new JsonArray()
        };

        using var response = await provider.PostChatCompletionAsync(request);

        Assert.True(
            mockHandler.LastRequest?.Headers.Contains("Authorization"),
            "Authorization header should be present");

        var authHeader = mockHandler.LastRequest?.Headers.GetValues("Authorization").FirstOrDefault();
        Assert.Equal("Bearer test-api-key", authHeader);
    }

    /// <summary>Mock handler for testing HTTP requests</summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
