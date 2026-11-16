using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed partial class AppConfig : IAppConfig
{
	public IGlobalConfig Global { get; init; } = new GlobalConfig();
	public Dictionary<string, IAgentConfig> Agents { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, IModelSourceConfig> ModelSources { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, string> ModelRoles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, IMcpUpstreamConfig> McpServers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public IReadOnlyList<ModelProfile> ModelProfiles { get; init; } = [];

	IReadOnlyDictionary<string, IAgentConfig> IAppConfig.Agents => Agents;
	IReadOnlyDictionary<string, IModelSourceConfig> IAppConfig.ModelSources => ModelSources;
	IReadOnlyDictionary<string, string> IAppConfig.ModelRoles => ModelRoles;
	IReadOnlyDictionary<string, IMcpUpstreamConfig> IAppConfig.McpServers => McpServers;

	public string DefaultAgentType => string.IsNullOrWhiteSpace(Global.DefaultAgentType)
		? "ask"
		: Global.DefaultAgentType;

	public static AppConfig Load(string path)
	{
		if (!File.Exists(path))
		{
			return new AppConfig();
		}

		JsonObject json = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
				   ?? throw new InvalidOperationException($"Config file must contain a JSON object: {path}");

		JsonObject globalNode = GetObject(json, "global") ?? new JsonObject();
		GlobalConfig global = ParseGlobal(globalNode);
		int? configurationId = GetInt(globalNode, "configurationId", "configuration_id");

		// Prefer a "models" section in the primary config file; fall back to the legacy
		// separate file pointed to by modelSourcesPath for backward compatibility.
		JsonObject? inlineModels = GetObject(json, "models");
		(Dictionary<string, IModelSourceConfig>? modelSources, Dictionary<string, string>? modelRoles) = inlineModels is not null
			? ParseModelSources(inlineModels, configurationId)
			: LoadModelSourcesFromFile(path, global.ModelSourcesPath, configurationId);

		return new AppConfig
		{
			Global = global,
			Agents = ParseAgents(GetObject(json, "agents") ?? GetObject(json, "modes") ?? []),
			ModelSources = modelSources,
			ModelRoles = modelRoles,
			McpServers = ParseMcpServers(GetObject(json, "mcpServers") ?? []),
			ModelProfiles = inlineModels is null ? [] : ParseModelProfiles(inlineModels),
		};
	}

	public IAgentConfig Agent(string agentType)
	{
		return Agents.TryGetValue(agentType, out IAgentConfig? settings) ? settings : new SimpleModelServer.Contracts.Dtos.Agents.AgentConfig();
	}

	/// <summary>
	///     Adds or replaces an MCP upstream in the live <see cref="McpServers" /> dictionary — the
	///     backing store for the admin "list/add/remove MCP servers" API. Mutates the dictionary
	///     instance in place rather than replacing it, so callers holding a reference to
	///     <see cref="McpServers" /> (e.g. <c>SimpleModelServer.Server.McpUpstreamRegistry</c>'s
	///     constructor-captured <c>upstreams</c>) see the change without needing to be re-wired.
	/// </summary>
	public void AddOrUpdateMcpServer(string name, IMcpUpstreamConfig serverConfig)
	{
		McpServers[name] = serverConfig;
	}

	/// <summary>Removes the named MCP upstream. Returns false if no upstream with that name was configured.</summary>
	public bool RemoveMcpServer(string name)
	{
		return McpServers.Remove(name);
	}

	/// <summary>
	///     Resolves the embedding model's name: <see cref="GlobalConfig.EmbedModel" /> override,
	///     then the 'embed' model role, then the 'nomic-embed-text' default. Mirrors
	///     SimpleModelPairGenerationService.EmbedModelName — kept here too so callers that only have an
	///     AppConfig (no SimpleModelPairGenerationService instance), like OllamaProcessManager and
	///     ModelHealthMonitor, can identify the embed model without a circular dependency.
	/// </summary>
	public string ResolveEmbedModelName()
	{
		return !string.IsNullOrWhiteSpace(Global.EmbedModel)
			? Global.EmbedModel
			: ModelRoles.TryGetValue("embed", out string? e)
				? e
				: "nomic-embed-text";
	}

	/// <summary>
	///     True if <paramref name="model" /> is the configured embedding model. Embedding-only
	///     models don't support Ollama's <c>/api/generate</c> endpoint — callers that ping/warm/
	///     unload a model by name must route to <c>/api/embed</c> instead when this is true.
	/// </summary>
	public bool IsEmbedModel(string model)
	{
		return string.Equals(model, ResolveEmbedModelName(), StringComparison.OrdinalIgnoreCase);
	}

	public bool IsKnownAgentType(string agentType)
	{
		return Agents.ContainsKey(agentType) ||
			   agentType.Equals("ask", StringComparison.OrdinalIgnoreCase) ||
			   agentType.Equals("debug", StringComparison.OrdinalIgnoreCase) ||
			   agentType.Equals("code", StringComparison.OrdinalIgnoreCase) ||
			   agentType.Equals("planning", StringComparison.OrdinalIgnoreCase);
	}

}
