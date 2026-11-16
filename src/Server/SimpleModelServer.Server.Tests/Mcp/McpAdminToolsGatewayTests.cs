using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;

using ModelContextProtocol.Client;

using System.Text.Json;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Exercises the self-management MCP tools (list/add/remove upstream servers, from
///     SimpleModelServer.Mcp's McpAdminTools) exactly as an MCP client would: connected to the
///     real /mcp gateway via <see cref="RouterTestFixture" />, alongside the proxied upstream
///     tools <see cref="McpUpstreamRegistryTests" /> covers — confirming the two tool sources
///     (reflection-registered WithTools&lt;McpAdminTools&gt;() and the
///     WithListToolsHandler/WithCallToolHandler pair backed by McpUpstreamRegistry) coexist in one
///     gateway, per RouterHttpServer.StartAsync's wiring.
/// </summary>
public sealed class McpAdminToolsGatewayTests
{
	[Fact]
	public async Task AdminTools_AreListedAlongsideProxiedUpstreamTools()
	{
		await using TestMcpServer upstream = await TestMcpServer.StartAsync();
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync(config => new AppConfig
		{
			Global = config.Global,
			Agents = config.Agents,
			ModelSources = config.ModelSources,
			ModelRoles = config.ModelRoles,
			McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase)
			{
				["echo-server"] = new McpUpstreamConfig { Path = upstream.McpUrl }
			}
		});
		await using McpClient client = await ConnectAsync(fixture.BaseAddress);

		IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

		Assert.Contains(tools, t => t.Name == "echo-server.echo");
		Assert.Contains(tools, t => t.Name == "list_mcp_servers");
		Assert.Contains(tools, t => t.Name == "add_mcp_server");
		Assert.Contains(tools, t => t.Name == "remove_mcp_server");
	}

	[Fact]
	public async Task ListMcpServers_ReportsConfiguredUpstream()
	{
		await using TestMcpServer upstream = await TestMcpServer.StartAsync();
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync(config => new AppConfig
		{
			Global = config.Global,
			Agents = config.Agents,
			ModelSources = config.ModelSources,
			ModelRoles = config.ModelRoles,
			McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase)
			{
				["echo-server"] = new McpUpstreamConfig { Path = upstream.McpUrl }
			}
		});
		await using McpClient client = await ConnectAsync(fixture.BaseAddress);

		JsonElement result = await CallToolAsJsonAsync(client, "list_mcp_servers", null);

		JsonElement server = result.GetProperty("mcpServers").GetProperty("echo-server");
		Assert.Equal("Http", server.GetProperty("transportType").GetString());
		Assert.Equal(upstream.McpUrl, server.GetProperty("path").GetString());
		Assert.True(server.GetProperty("connected").GetBoolean());
		Assert.Equal(1, server.GetProperty("toolCount").GetInt32());
	}

	[Fact]
	public async Task AddMcpServer_ThenListToolsAsync_IncludesTheNewUpstreamsTools()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
		await using TestMcpServer upstream = await TestMcpServer.StartAsync();
		await using McpClient client = await ConnectAsync(fixture.BaseAddress);

		JsonElement addResult = await CallToolAsJsonAsync(client, "add_mcp_server", new Dictionary<string, object?>
		{
			["name"] = "new-echo",
			["path"] = upstream.McpUrl
		});
		Assert.True(addResult.GetProperty("success").GetBoolean());

		IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
		Assert.Contains(tools, t => t.Name == "new-echo.echo");

		CallToolResultProbe echoResult = await CallRawAsync(client, "new-echo.echo", new Dictionary<string, object?> { ["message"] = "hi" });
		Assert.False(echoResult.IsError);
		Assert.Equal("echo: hi", echoResult.Text);
	}

	[Fact]
	public async Task AddMcpServer_NameWithDot_ReturnsFailureEnvelope()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
		await using McpClient client = await ConnectAsync(fixture.BaseAddress);

		JsonElement result = await CallToolAsJsonAsync(client, "add_mcp_server", new Dictionary<string, object?>
		{
			["name"] = "bad.name",
			["path"] = "http://localhost:9999/mcp"
		});

		Assert.False(result.GetProperty("success").GetBoolean());
	}

	[Fact]
	public async Task RemoveMcpServer_AfterAdd_RemovesItsTools()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
		await using TestMcpServer upstream = await TestMcpServer.StartAsync();
		await using McpClient client = await ConnectAsync(fixture.BaseAddress);

		await CallToolAsJsonAsync(client, "add_mcp_server", new Dictionary<string, object?>
		{
			["name"] = "to-remove",
			["path"] = upstream.McpUrl
		});

		JsonElement removeResult = await CallToolAsJsonAsync(client, "remove_mcp_server", new Dictionary<string, object?> { ["name"] = "to-remove" });
		Assert.True(removeResult.GetProperty("success").GetBoolean());

		IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
		Assert.DoesNotContain(tools, t => t.Name == "to-remove.echo");
	}

	[Fact]
	public async Task RemoveMcpServer_UnknownName_ReturnsFailureEnvelope()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
		await using McpClient client = await ConnectAsync(fixture.BaseAddress);

		JsonElement result = await CallToolAsJsonAsync(client, "remove_mcp_server", new Dictionary<string, object?> { ["name"] = "never-existed" });

		Assert.False(result.GetProperty("success").GetBoolean());
	}

	private static Task<McpClient> ConnectAsync(string gatewayBaseAddress)
		=> McpClient.CreateAsync(
			new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri($"{gatewayBaseAddress}/mcp") }),
			cancellationToken: TestContext.Current.CancellationToken);

	private static async Task<JsonElement> CallToolAsJsonAsync(McpClient client, string toolName, IReadOnlyDictionary<string, object?>? arguments)
	{
		CallToolResultProbe probe = await CallRawAsync(client, toolName, arguments);
		Assert.False(probe.IsError);
		return JsonDocument.Parse(probe.Text).RootElement;
	}

	private static async Task<CallToolResultProbe> CallRawAsync(McpClient client, string toolName, IReadOnlyDictionary<string, object?>? arguments)
	{
		ModelContextProtocol.Protocol.CallToolResult result = await client.CallToolAsync(
			toolName, arguments, cancellationToken: TestContext.Current.CancellationToken);
		string text = ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text;
		return new CallToolResultProbe(result.IsError == true, text);
	}

	private readonly record struct CallToolResultProbe(bool IsError, string Text);
}
