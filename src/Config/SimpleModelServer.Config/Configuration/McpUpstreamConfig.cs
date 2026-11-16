using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed class McpUpstreamConfig : IMcpUpstreamConfig
{
	public McpTransportType TransportType { get; init; } = McpTransportType.Http;
	public string Path { get; init; } = "";
	public IReadOnlyList<string> Args { get; init; } = [];
}
