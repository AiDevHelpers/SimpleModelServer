using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Configuration;

namespace SimpleModelServer.Config.Configuration;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716",
    Justification = "Global (the global settings section) is used as config.Global.* across hundreds of call sites in this C#-only codebase; renaming for cross-language interop that doesn't apply here would be a large, high-risk change for no functional benefit.")]
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

    /// <summary>Adds a new MCP upstream or replaces an existing one with the same name (case-insensitive).</summary>
    void AddOrUpdateMcpServer(string name, IMcpUpstreamConfig serverConfig);

    /// <summary>Removes the named MCP upstream. Returns false if no upstream with that name was configured.</summary>
    bool RemoveMcpServer(string name);
}
