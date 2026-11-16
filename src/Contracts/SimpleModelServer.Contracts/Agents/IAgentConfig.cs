namespace SimpleModelServer.Contracts.Agents;

using System.Text.Json.Nodes;

public interface IAgentConfig
{
	string SystemPrompt { get; set; }
	double? Temperature { get; set; }
	JsonNode? Sampling { get; set; }
}




