using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed partial class AppConfig
{
	private static Dictionary<string, IAgentConfig> ParseAgents(JsonObject agents)
	{
		Dictionary<string, IAgentConfig> result = new(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, JsonNode?> pair in agents)
		{
			if (pair.Value is JsonObject agent)
			{
				result[pair.Key] = new SimpleModelServer.Contracts.Dtos.Agents.AgentConfig
				{
					SystemPrompt = GetString(agent, "systemPrompt", "system_prompt", "system") ?? "",
					Temperature = GetDouble(agent, "temperature", "tempature"),
					Sampling = GetNode(agent, "sampling")?.DeepClone()
				};
			}
		}

		return result;
	}

}
