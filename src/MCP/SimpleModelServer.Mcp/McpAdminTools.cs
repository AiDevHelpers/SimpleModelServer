using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;

using ModelContextProtocol.Server;

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleModelServer.Mcp;

/// <summary>
///     Self-management MCP tools for the gateway's own upstream MCP server list — list, add, and
///     remove entries in <see cref="IAppConfig.McpServers" />. Registered directly into the
///     router's MCP server via <c>WithTools&lt;McpAdminTools&gt;()</c>
///     (see <c>SimpleModelServer.Server.RouterHttpServer.StartAsync</c>), alongside the
///     <see cref="IMcpUpstreamRegistry" />-backed proxy tools — so these three tools are always
///     present in the gateway's tool list, with no upstream registration of their own required.
///     A fresh instance is constructed per tool call (standard MCP SDK behavior for
///     constructor-injected <c>[McpServerToolType]</c> classes), so state lives entirely in the
///     injected <see cref="IAppConfig" />/<see cref="IMcpUpstreamRegistry" /> singletons, not on
///     this class.
/// </summary>
[McpServerToolType]
public sealed class McpAdminTools(IAppConfig config, IMcpUpstreamRegistry registry)
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	[McpServerTool(Name = "list_mcp_servers"), Description("Lists every MCP upstream server configured on this gateway, with its transport, path, and live connection status.")]
	public string ListMcpServers()
	{
		IReadOnlyDictionary<string, McpUpstreamStatus> status = registry.GetStatus();

		var mcpServers = config.McpServers.ToDictionary(
			kvp => kvp.Key,
			kvp =>
			{
				status.TryGetValue(kvp.Key, out McpUpstreamStatus s);
				return new McpServerListEntry(kvp.Value.TransportType.ToString(), kvp.Value.Path, kvp.Value.Args, s.Connected, s.ToolCount);
			});

		return JsonSerializer.Serialize(new { mcpServers }, JsonOptions);
	}

	[McpServerTool(Name = "add_mcp_server"), Description("Adds a new MCP upstream server, or replaces an existing one with the same name. transportType defaults to \"Http\" when omitted. HTTP upstreams connect immediately — no restart required. name cannot contain '.' (tools are namespaced as '<name>.<toolName>').")]
	public async Task<string> AddMcpServerAsync(
		[Description("Unique upstream name, no '.' characters.")] string name,
		[Description("The upstream's HTTP endpoint URL, or executable file path for a stdio upstream.")] string path,
		[Description("How the gateway connects to this upstream: \"Http\" (default) or \"Stdio\".")] string? transportType = null,
		[Description("Optional command-line arguments for a stdio upstream.")] string[]? args = null)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return Failure("Missing or empty 'name'.");
		}

		if (name.Contains('.', StringComparison.Ordinal))
		{
			return Failure("'name' cannot contain '.' — the gateway namespaces tools as '<name>.<toolName>'.");
		}

		if (string.IsNullOrWhiteSpace(path))
		{
			return Failure("Missing or empty 'path'.");
		}

		if (!TryParseTransportType(transportType, out McpTransportType parsedTransportType))
		{
			return Failure($"'transportType' must be one of: {string.Join(", ", Enum.GetNames<McpTransportType>())}");
		}

		IMcpUpstreamConfig serverConfig = new McpUpstreamConfig
		{
			TransportType = parsedTransportType,
			Path = path,
			Args = args ?? []
		};

		config.AddOrUpdateMcpServer(name, serverConfig);
		await registry.DisconnectAsync(name);
		await registry.ConnectAsync(name, serverConfig);

		return JsonSerializer.Serialize(new { success = true, name, message = $"MCP server '{name}' added." }, JsonOptions);
	}

	[McpServerTool(Name = "remove_mcp_server"), Description("Removes a configured MCP upstream server and disconnects it from the live gateway.")]
	public async Task<string> RemoveMcpServerAsync([Description("The upstream's configured name.")] string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return Failure("Missing or empty 'name'.");
		}

		if (!config.RemoveMcpServer(name))
		{
			return Failure($"No MCP server named '{name}' is configured.");
		}

		await registry.DisconnectAsync(name);

		return JsonSerializer.Serialize(new { success = true, name, message = $"MCP server '{name}' removed." }, JsonOptions);
	}

	private string Failure(string message)
		=> JsonSerializer.Serialize(new { success = false, error = message }, JsonOptions);

	private static bool TryParseTransportType(string? value, out McpTransportType transportType)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			transportType = McpTransportType.Http;
			return true;
		}

		foreach (McpTransportType candidate in Enum.GetValues<McpTransportType>())
		{
			if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
			{
				transportType = candidate;
				return true;
			}
		}

		transportType = default;
		return false;
	}

	private sealed record McpServerListEntry(string TransportType, string Path, IReadOnlyList<string> Args, bool Connected, int ToolCount);
}
