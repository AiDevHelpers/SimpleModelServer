using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Configuration;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Exercises the MCP gateway end to end, no mocks — same real-HTTP philosophy as
///     <see cref="RouterTestFixture" />: a genuine in-process upstream MCP server
///     (<see cref="TestMcpServer" />) behind a real Kestrel host, a real <see cref="RouterHttpServer" />
///     configured to proxy it (via <see cref="RouterTestFixture" />'s <c>configureConfig</c> hook),
///     and a real downstream <see cref="McpClient" /> calling the gateway's own /mcp endpoint —
///     exercising <see cref="McpUpstreamRegistry" />'s aggregation/routing exactly as a real agent
///     client would, rather than hand-constructing SDK request-context objects.
/// </summary>
public sealed class McpUpstreamRegistryTests
{
	[Fact]
	public async Task Gateway_AggregatesUpstreamToolWithNamespacedNameAndRoundTripsCalls()
	{
		await using TestMcpServer upstream = await TestMcpServer.StartAsync();
		await using RouterTestFixture fixture = await CreateFixtureWithUpstreamAsync(upstream);
		await using McpClient client = await ConnectAsync(fixture.BaseAddress);

		IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
		Assert.Contains(tools, t => t.Name == "echo-server.echo");

		CallToolResult result = await client.CallToolAsync(
			"echo-server.echo",
			new Dictionary<string, object?> { ["message"] = "hi" },
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.IsError == true);
		TextContentBlock text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
		Assert.Equal("echo: hi", text.Text);
	}

	[Fact]
	public async Task Gateway_SkipsUnreachableUpstreamInsteadOfFailingStartup()
	{
		int closedPort = GetFreeTcpPort();

		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync(config => new AppConfig
		{
			Global = config.Global,
			Agents = config.Agents,
			ModelSources = config.ModelSources,
			ModelRoles = config.ModelRoles,
			McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase)
			{
				["unreachable"] = new McpUpstreamConfig { Path = $"http://127.0.0.1:{closedPort}/mcp" }
			}
		});

		// The router must still come up healthy (RouterTestFixture.CreateAsync already waits for
		// /api/version) — an unreachable upstream must not take down the whole gateway.
		await using McpClient client = await ConnectAsync(fixture.BaseAddress);
		IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

		// No tools from the unreachable upstream, but the always-on self-management tools
		// (McpAdminTools, see McpAdminToolsGatewayTests) are still present — they're
		// reflection-registered via WithTools<McpAdminTools>(), independent of any upstream.
		Assert.DoesNotContain(tools, t => t.Name.StartsWith("unreachable.", StringComparison.Ordinal));
	}

	private static async Task<RouterTestFixture> CreateFixtureWithUpstreamAsync(TestMcpServer upstream)
	{
		return await RouterTestFixture.CreateAsync(config => new AppConfig
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
	}

	private static Task<McpClient> ConnectAsync(string gatewayBaseAddress)
		=> McpClient.CreateAsync(
			new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri($"{gatewayBaseAddress}/mcp") }),
			cancellationToken: TestContext.Current.CancellationToken);

	private static int GetFreeTcpPort()
	{
		TcpListener tcpListener = new(IPAddress.Loopback, 0);
		tcpListener.Start();
		int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
		tcpListener.Stop();
		return port;
	}
}

/// <summary>A tiny real MCP server (one tool) hosted on an ephemeral loopback port, standing in for a real upstream.</summary>
internal sealed class TestMcpServer : IAsyncDisposable
{
	private readonly WebApplication app;

	public string McpUrl { get; }

	private TestMcpServer(WebApplication app, string mcpUrl)
	{
		this.app = app;
		McpUrl = mcpUrl;
	}

	public static async Task<TestMcpServer> StartAsync()
	{
		int port = GetFreeTcpPort();

		WebApplicationBuilder builder = WebApplication.CreateBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
		builder.Services.AddMcpServer().WithHttpTransport().WithTools<EchoTool>();

		WebApplication app = builder.Build();
		app.MapMcp("/mcp");
		await app.StartAsync();

		return new TestMcpServer(app, $"http://127.0.0.1:{port}/mcp");
	}

	public async ValueTask DisposeAsync()
	{
		await app.StopAsync();
		await app.DisposeAsync();
	}

	private static int GetFreeTcpPort()
	{
		TcpListener tcpListener = new(IPAddress.Loopback, 0);
		tcpListener.Start();
		int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
		tcpListener.Stop();
		return port;
	}
}

[McpServerToolType]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812",
	Justification = "Instantiated via reflection by the MCP SDK's WithTools<EchoTool>() registration, not by application code.")]
internal sealed class EchoTool
{
	[McpServerTool, Description("Echoes the input message back, prefixed with 'echo: '.")]
	public static string Echo(string message) => $"echo: {message}";
}
