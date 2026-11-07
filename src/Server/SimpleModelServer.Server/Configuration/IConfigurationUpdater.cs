using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Config.Configuration;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server.Configuration;

/// <summary>
///     Applies validated configuration changes to the in-memory AppConfig.
/// </summary>
internal interface IConfigurationUpdater
{
	/// <summary>
	///     Updates a global configuration setting and returns whether a restart is required.
	/// </summary>
	UpdateResult UpdateGlobalSetting(IGlobalConfig config, string settingName, object? value);

	/// <summary>
	///     Updates a field (e.g. "systemPrompt", "temperature") on the named agent's live config.
	/// </summary>
	UpdateResult UpdateAgentSetting(IReadOnlyDictionary<string, IAgentConfig> agents, string agentName, string fieldName, object? value);
}
