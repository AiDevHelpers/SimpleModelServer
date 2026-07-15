namespace SimpleModelServer.Contracts.Configuration;

/// <summary>
///     One upstream MCP server the gateway proxies/aggregates — see
///     <c>SimpleModelServer.Server.McpUpstreamRegistry</c>. HTTP (streamable-http) only for now;
///     no real stdio MCP servers exist to target yet, so that's deferred until one does.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056",
    Justification = "Url is a plain string bound from JSON config and used verbatim in HttpClientTransportOptions/string interpolation; changing to System.Uri would complicate config binding for no functional benefit.")]
public interface IMcpUpstreamConfig
{
    /// <summary>The upstream's MCP HTTP endpoint, e.g. "http://localhost:11436/mcp".</summary>
    string Url { get; }
}
