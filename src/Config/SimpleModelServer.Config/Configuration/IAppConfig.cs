using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Configuration;

namespace SimpleModelServer.Config.Configuration;

public interface IAppConfig
{
    IGlobalConfig Global { get; }
    IReadOnlyDictionary<string, IAgentConfig> Agents { get; }
    IReadOnlyDictionary<string, IModelSourceConfig> ModelSources { get; }
    IReadOnlyDictionary<string, string> ModelRoles { get; }
    IReadOnlyDictionary<string, IMcpUpstreamConfig> McpServers { get; }
    string DefaultAgentType { get; }

    string ResolveEmbedModelName();
    bool IsEmbedModel(string model);
    bool IsKnownAgentType(string agentType);
    IAgentConfig Agent(string agentType);
}
