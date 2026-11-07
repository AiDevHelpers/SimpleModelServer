using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Dtos.Settings;


namespace SimpleModelServer.Contracts.Dtos.Agents;

using SimpleModelServer.Contracts.Models;

public class AgentConfig : IAgentConfig
{
	public string SystemPrompt { get; set; } = "";
	public double? Temperature { get; set; }
	public JsonNode? Sampling { get; set; }
}





