namespace SimpleModelServer.Contracts.Configuration;

/// <summary>Point-in-time status of one configured MCP upstream — see <see cref="IMcpUpstreamRegistry.GetStatus" />.</summary>
public readonly record struct McpUpstreamStatus(McpTransportType TransportType, string Path, bool Connected, int ToolCount);

/// <summary>
///     Connects/disconnects individual MCP upstreams live (no restart) and reports their status.
///     Implemented by <c>SimpleModelServer.Server.McpUpstreamRegistry</c>, the gateway's actual
///     tool aggregator; exposed here as a public interface so callers outside that assembly —
///     e.g. <c>SimpleModelServer.Mcp</c>'s self-management tools — can drive it without depending
///     on internal server types.
/// </summary>
public interface IMcpUpstreamRegistry
{
	/// <summary>
	///     Connects to a single upstream and aggregates its tools. An unreachable upstream, or one
	///     configured with a transport not yet supported (see <see cref="McpTransportType" />), is
	///     logged and left disconnected rather than throwing.
	/// </summary>
	Task ConnectAsync(string serverName, IMcpUpstreamConfig upstreamConfig, CancellationToken ct = default);

	/// <summary>
	///     Disconnects and removes an upstream's client and aggregated tools. Safe to call for an
	///     upstream that was never connected — it's just a no-op in that case.
	/// </summary>
	Task DisconnectAsync(string serverName);

	/// <summary>Per-upstream status for display purposes — configured transport/path, whether it's connected, and how many tools it contributed.</summary>
	IReadOnlyDictionary<string, McpUpstreamStatus> GetStatus();
}
