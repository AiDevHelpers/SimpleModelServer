using System.Linq;
using System.Net;
using System.Text.Json;

using Xunit;
using Xunit.Sdk;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers every GET endpoint documented in docs/router-contract.md. Each test hits the real
///     router over HTTP (via <see cref="RouterTestFixture" />) and asserts on status code and
///     JSON shape exactly as an external caller would observe them.
/// </summary>
public sealed class GetEndpointTests
{
	[Fact]
	public async Task Health_AllHealthy_Returns200Ok()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
		Assert.Equal("healthy", doc.RootElement.GetProperty("ollama").GetString());
		Assert.Equal("healthy", doc.RootElement.GetProperty("models").GetProperty("draft").GetString());
		Assert.Equal("healthy", doc.RootElement.GetProperty("models").GetProperty("main").GetString());
		Assert.True(doc.RootElement.TryGetProperty("agent", out _));
	}

	[Fact]
	public async Task HealthReady_AllHealthy_Returns200Ok()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
	}

	[Fact]
	public async Task ApiVersion_ReturnsStaticVersion()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("0.1.0", doc.RootElement.GetProperty("version").GetString());
	}

	[Fact]
	public async Task ApiTags_ReturnsRouterModelName()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/api/tags", UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		JsonElement model = doc.RootElement.GetProperty("models")[0];
		Assert.Equal("TestRouterModel", model.GetProperty("name").GetString());
		Assert.Equal(0, model.GetProperty("size").GetInt32());
	}

	[Fact]
	public async Task ApiPs_ProxiesToOllama()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/api/ps", UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(doc.RootElement.TryGetProperty("models", out _));
	}

	[Fact]
	public async Task ApiCapacity_ReturnsHardwareSettings()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/api/capacity", UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(doc.RootElement.TryGetProperty("cpuThreads", out _));
		Assert.True(doc.RootElement.TryGetProperty("gpuLayers", out _));
		Assert.True(doc.RootElement.TryGetProperty("gpu", out _));
		Assert.Equal(1, doc.RootElement.GetProperty("maxConcurrentSummaryRequests").GetInt32());
	}

	[Theory]
	[InlineData("/v1/models")]
	[InlineData("/models")]
	public async Task ModelsEndpoints_ReturnOpenAiCompatibleList(string path)
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("list", doc.RootElement.GetProperty("object").GetString());
		JsonElement model = doc.RootElement.GetProperty("data")[0];
		Assert.Equal("TestRouterModel", model.GetProperty("id").GetString());
		Assert.Equal("local", model.GetProperty("owned_by").GetString());
	}

	[Fact]
	public async Task ApiSystemInfo_ReturnsCapabilitiesAndConfiguration()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/api/system/info", UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		// Verify capabilities structure exists (PascalCase in JSON due to default JsonSerializer behavior)
		Assert.True(doc.RootElement.TryGetProperty("Capabilities", out var capabilities));

		// Verify capabilities has expected properties (PascalCase)
		Assert.True(capabilities.TryGetProperty("Models", out var models));
		Assert.True(models.ValueKind == System.Text.Json.JsonValueKind.Array);

		Assert.True(capabilities.TryGetProperty("AgentTypes", out var agentTypes));
		Assert.True(agentTypes.ValueKind == System.Text.Json.JsonValueKind.Array);
		Assert.True(agentTypes.GetArrayLength() > 0);  // Should have at least "code"

		Assert.True(capabilities.TryGetProperty("Hardware", out var hardware));
		Assert.True(hardware.TryGetProperty("GpuAvailable", out _));
		Assert.True(hardware.TryGetProperty("MaxConcurrentSummaryRequests", out _));

		Assert.True(capabilities.TryGetProperty("Features", out var features));
		Assert.True(features.ValueKind == System.Text.Json.JsonValueKind.Array);

		// Verify configuration structure exists
		Assert.True(doc.RootElement.TryGetProperty("Configuration", out var configuration));

		// Verify configuration has expected sub-objects
		Assert.True(configuration.TryGetProperty("Global", out var globalConfig));
		Assert.True(globalConfig.TryGetProperty("RouterPort", out _));
		Assert.True(globalConfig.TryGetProperty("OllamaPort", out _));

		Assert.True(configuration.TryGetProperty("ModelRoles", out var modelRoles));
		Assert.True(modelRoles.ValueKind == System.Text.Json.JsonValueKind.Object);

		Assert.True(configuration.TryGetProperty("Agents", out var agents));
		Assert.True(agents.ValueKind == System.Text.Json.JsonValueKind.Object);

		// Verify timestamp is present
		Assert.True(doc.RootElement.TryGetProperty("Timestamp", out _));
	}

	[Fact]
	public async Task UnknownGetPath_Returns404()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/nonexistent", UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
		Assert.Equal("not found", doc.RootElement.GetProperty("error").GetString());
	}

	[Theory]
	[InlineData("/health")]
	[InlineData("/api/version")]
	public async Task EveryResponse_IncludesCorsHeaders(string path)
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

		Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
		Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
	}

	[Fact]
	public async Task Options_AnyPath_Returns200EmptyBody()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpRequestMessage request = new(HttpMethod.Options, "/anything/at/all");
		using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}
}
