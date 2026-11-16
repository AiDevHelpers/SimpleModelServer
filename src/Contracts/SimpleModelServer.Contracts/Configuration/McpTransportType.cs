namespace SimpleModelServer.Contracts.Configuration;

/// <summary>
///     How the gateway connects to an upstream MCP server — see <see cref="IMcpUpstreamConfig" />.
/// </summary>
public enum McpTransportType
{
	/// <summary>
	///     Streamable-HTTP MCP server reached over the network.
	///     <see cref="IMcpUpstreamConfig.Path" /> is the server's HTTP endpoint URL.
	/// </summary>
	Http = 0,

	/// <summary>
	///     Local MCP server launched as a stdio subprocess. <see cref="IMcpUpstreamConfig.Path" />
	///     is the executable's file path. Not yet connected by <c>McpUpstreamRegistry</c> — see
	///     that class's remarks — but accepted here so it can be configured ahead of that support
	///     landing.
	/// </summary>
	Stdio = 1,
}
