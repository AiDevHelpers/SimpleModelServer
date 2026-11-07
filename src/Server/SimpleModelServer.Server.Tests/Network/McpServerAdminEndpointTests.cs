using System.Net;
using System.Text;
using System.Text.Json;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers the admin MCP-server settings API — GET /admin/mcp-servers (list), POST
///     /admin/mcp-servers (add/update), and POST /admin/mcp-servers/remove — over real HTTP via
///     <see cref="RouterTestFixture" />, the same end-to-end style as <see cref="GetEndpointTests" />
///     and <see cref="PostEndpointTests" />.
/// </summary>
public sealed class McpServerAdminEndpointTests
{
	private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

	[Fact]
	public async Task List_NoServersConfigured_ReturnsEmptyDictionary()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/admin/mcp-servers", UriKind.Relative), TestContext.Current.CancellationToken);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument doc = JsonDocument.Parse(body);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Empty(doc.RootElement.GetProperty("mcpServers").EnumerateObject());
	}

	[Fact]
	public async Task Add_ThenList_ReturnsTheNewServer()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
		await using TestMcpServer upstream = await TestMcpServer.StartAsync();

		using HttpResponseMessage addResponse = await fixture.Client.PostAsync(
			new Uri("/admin/mcp-servers", UriKind.Relative),
			JsonBody($$"""{"name":"echo-server","transportType":"Http","path":"{{upstream.McpUrl}}"}"""),
			TestContext.Current.CancellationToken);
		string addBody = await addResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument addDoc = JsonDocument.Parse(addBody);

		Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
		Assert.True(addDoc.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal("echo-server", addDoc.RootElement.GetProperty("name").GetString());

		using HttpResponseMessage listResponse = await fixture.Client.GetAsync(new Uri("/admin/mcp-servers", UriKind.Relative), TestContext.Current.CancellationToken);
		string listBody = await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument listDoc = JsonDocument.Parse(listBody);

		JsonElement server = listDoc.RootElement.GetProperty("mcpServers").GetProperty("echo-server");
		Assert.Equal("Http", server.GetProperty("transportType").GetString());
		Assert.Equal(upstream.McpUrl, server.GetProperty("path").GetString());
		Assert.True(server.GetProperty("connected").GetBoolean());
		Assert.Equal(1, server.GetProperty("toolCount").GetInt32());
	}

	[Fact]
	public async Task Add_StdioTransport_IsConfiguredButNotYetConnected()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage addResponse = await fixture.Client.PostAsync(
			new Uri("/admin/mcp-servers", UriKind.Relative),
			JsonBody("""{"name":"local-tool","transportType":"Stdio","path":"C:\\tools\\server.exe","args":["--flag"]}"""),
			TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

		using HttpResponseMessage listResponse = await fixture.Client.GetAsync(new Uri("/admin/mcp-servers", UriKind.Relative), TestContext.Current.CancellationToken);
		JsonDocument listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

		JsonElement server = listDoc.RootElement.GetProperty("mcpServers").GetProperty("local-tool");
		Assert.Equal("Stdio", server.GetProperty("transportType").GetString());
		Assert.False(server.GetProperty("connected").GetBoolean());
	}

	[Fact]
	public async Task Add_MissingName_Returns400()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.PostAsync(
			new Uri("/admin/mcp-servers", UriKind.Relative),
			JsonBody("""{"path":"http://localhost:9999/mcp"}"""),
			TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task Add_MissingPath_Returns400()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.PostAsync(
			new Uri("/admin/mcp-servers", UriKind.Relative),
			JsonBody("""{"name":"broken"}"""),
			TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task Add_NameContainingDot_Returns400()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.PostAsync(
			new Uri("/admin/mcp-servers", UriKind.Relative),
			JsonBody("""{"name":"bad.name","path":"http://localhost:9999/mcp"}"""),
			TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task Add_UnknownTransportType_Returns400()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.PostAsync(
			new Uri("/admin/mcp-servers", UriKind.Relative),
			JsonBody("""{"name":"bad-transport","transportType":"Carrier-Pigeon","path":"http://localhost:9999/mcp"}"""),
			TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task Remove_ExistingServer_RemovesItFromList()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		await fixture.Client.PostAsync(
			new Uri("/admin/mcp-servers", UriKind.Relative),
			JsonBody("""{"name":"to-remove","transportType":"Stdio","path":"C:\\tools\\server.exe"}"""),
			TestContext.Current.CancellationToken);

		using HttpResponseMessage removeResponse = await fixture.Client.PostAsync(
			new Uri("/admin/mcp-servers/remove", UriKind.Relative),
			JsonBody("""{"name":"to-remove"}"""),
			TestContext.Current.CancellationToken);
		string removeBody = await removeResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		JsonDocument removeDoc = JsonDocument.Parse(removeBody);

		Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
		Assert.True(removeDoc.RootElement.GetProperty("success").GetBoolean());

		using HttpResponseMessage listResponse = await fixture.Client.GetAsync(new Uri("/admin/mcp-servers", UriKind.Relative), TestContext.Current.CancellationToken);
		JsonDocument listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

		Assert.False(listDoc.RootElement.GetProperty("mcpServers").TryGetProperty("to-remove", out _));
	}

	[Fact]
	public async Task Remove_UnknownServer_Returns404()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		using HttpResponseMessage response = await fixture.Client.PostAsync(
			new Uri("/admin/mcp-servers/remove", UriKind.Relative),
			JsonBody("""{"name":"never-existed"}"""),
			TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}
}
