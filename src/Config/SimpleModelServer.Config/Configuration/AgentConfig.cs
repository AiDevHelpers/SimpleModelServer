using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed class AgentConfig : IAgentConfig
{
	public string SystemPrompt { get; set; } = "";
	public double? Temperature { get; set; }
	public JsonNode? Sampling { get; set; }
}
