using System.Net;
using System.Text;
using System.Text.Json;

using Xunit;
using Xunit.Sdk;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers the degraded/unhealthy branches of /health and /health/ready (simulated by pointing
///     the fixture's OllamaPort at a closed port so every probe fails), plus a couple of
///     AgentTypeResolver's documented precedence rules exercised through the real HTTP surface —
///     see <c>AgentTypeResolverTests</c> for the direct unit-test coverage of precedence ordering
///     itself, this class only spot-checks it end to end through the router.
/// </summary>
public sealed class HealthAndAgentRoutingTests
{
	private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

	[Fact]
	public async Task HealthReady_Returns503_WhenOllamaUnreachable()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		// Kill the fake backend after the fixture/router are already up, so subsequent probes
		// against OllamaPort fail outright — simulates Ollama being down.
		fixture.Ollama.Dispose();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.Equal("unhealthy", doc.RootElement.GetProperty("ollama").GetString());
	}

	[Fact]
	public async Task ApiGenerate_QueryParamAgentType_IsHonored()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
		fixture.Ollama.GenerateResponseText = "from query param routing";

		using HttpResponseMessage response = await fixture.Client.PostAsync(
			new Uri("/api/generate?agent_type=code", UriKind.Relative), JsonBody("""{"prompt":"anything at all","stream":false}"""));
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("from query param routing", doc.RootElement.GetProperty("response").GetString());
	}

	[Fact]
	public async Task ApiGenerate_UnknownAgentType_FallsBackToDefault()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
		fixture.Ollama.GenerateResponseText = "default agent response";

		using HttpRequestMessage request = new(HttpMethod.Post, "/api/generate")
		{
			Content = JsonBody("""{"prompt":"nothing special here","stream":false}""")
		};
		request.Headers.Add("X-Agent-Type", "totally-unknown-agent");

		using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		// "totally-unknown-agent" isn't a known agent type, so AgentTypeResolver falls through
		// every candidate and lands on config.DefaultAgentType ("code" in the fixture) — the
		// request should still succeed rather than error.
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("default agent response", doc.RootElement.GetProperty("response").GetString());
	}
}
