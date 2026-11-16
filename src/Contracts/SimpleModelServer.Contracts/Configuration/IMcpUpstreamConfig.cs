namespace SimpleModelServer.Contracts.Configuration;

/// <summary>
///     One upstream MCP server the gateway proxies/aggregates — see
///     <c>SimpleModelServer.Server.McpUpstreamRegistry</c>. Keyed by name in
///     <see cref="IAppConfig.McpServers" />; HTTP (streamable-http) upstreams are actually
///     connected today, <see cref="McpTransportType.Stdio" /> can be configured but isn't wired
///     up to a real process yet — see <see cref="McpTransportType" />.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056",
	Justification = "Path is a plain string bound from JSON config and used verbatim in HttpClientTransportOptions/string interpolation; changing to System.Uri would complicate config binding for no functional benefit, and it doubles as a filesystem path for stdio upstreams.")]
public interface IMcpUpstreamConfig
{
	/// <summary>How the gateway connects to this upstream.</summary>
	McpTransportType TransportType { get; }

	/// <summary>The upstream's HTTP endpoint URL (<see cref="McpTransportType.Http" />) or executable file path (<see cref="McpTransportType.Stdio" />).</summary>
	string Path { get; }

	/// <summary>Optional command-line arguments passed when launching a <see cref="McpTransportType.Stdio" /> upstream. Empty for <see cref="McpTransportType.Http" />.</summary>
	IReadOnlyList<string> Args { get; }
}
