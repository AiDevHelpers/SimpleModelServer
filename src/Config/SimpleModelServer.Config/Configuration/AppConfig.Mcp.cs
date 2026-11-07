using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed partial class AppConfig
{
	/// <summary>Parse the "mcpServers" JsonObject — upstream MCP servers the gateway aggregates.</summary>
	private static Dictionary<string, IMcpUpstreamConfig> ParseMcpServers(JsonObject mcpServersNode)
	{
		Dictionary<string, IMcpUpstreamConfig> servers = new(StringComparer.OrdinalIgnoreCase);

		foreach (KeyValuePair<string, JsonNode?> pair in mcpServersNode)
		{
			if (pair.Value is not JsonObject serverObj)
			{
				continue;
			}

			// "url" is the legacy key name from before stdio upstreams were configurable;
			// "path" is the current, transport-agnostic name — either is accepted.
			string? path = GetString(serverObj, "path", "url");
			if (string.IsNullOrWhiteSpace(path))
			{
				continue;
			}

			servers[pair.Key] = new McpUpstreamConfig
			{
				TransportType = ParseMcpTransportType(GetString(serverObj, "transportType", "transport_type", "transport")),
				Path = path,
				Args = ParseStringArray(GetNode(serverObj, "args"))
			};
		}

		return servers;
	}

	/// <summary>
	///     Parses an MCP upstream's <c>transportType</c>, defaulting to <see cref="McpTransportType.Http" />
	///     for any missing/unrecognized value — every upstream configured before
	///     <see cref="McpTransportType.Stdio" /> existed was HTTP, so this keeps those configs working
	///     unchanged.
	/// </summary>
	private static McpTransportType ParseMcpTransportType(string? value)
	{
		return value?.Trim().ToUpperInvariant() switch
		{
			"STDIO" => McpTransportType.Stdio,
			_ => McpTransportType.Http
		};
	}

}
