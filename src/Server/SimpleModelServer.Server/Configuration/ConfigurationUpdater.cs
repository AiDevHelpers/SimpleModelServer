using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Agents;

namespace SimpleModelServer.Server.Configuration;

/// <summary>
///     Applies validated configuration changes to the in-memory AppConfig.
///     Setter logic lives in <see cref="GlobalSettingRegistry" /> alongside each setting's
///     validation rule, so the two can't drift apart the way two independent switch statements
///     could.
/// </summary>
internal sealed class ConfigurationUpdater : IConfigurationUpdater
{
	private readonly IConfigurationManager configurationManager;

	public ConfigurationUpdater(IConfigurationManager configurationManager)
	{
		this.configurationManager = configurationManager ?? throw new ArgumentNullException(nameof(configurationManager));
	}

	/// <summary>
	///     Updates a global configuration setting and returns whether a restart is required.
	/// </summary>
	public UpdateResult UpdateGlobalSetting(IGlobalConfig config, string settingName, object? value)
	{
		bool requiresRestart = configurationManager.RequiresRestart($"global.{settingName}");

		SettingDescriptor? descriptor = GlobalSettingRegistry.Find(settingName);
		if (descriptor is null)
		{
			return new UpdateResult(false, $"Unknown setting: {settingName}", requiresRestart);
		}

		try
		{
			descriptor.Apply(config, value);
			return new UpdateResult(true, null, requiresRestart);
		}
		catch (Exception ex)
		{
			return new UpdateResult(false, ex.Message, requiresRestart);
		}
	}

	/// <summary>
	///     Updates a field (e.g. "systemPrompt", "temperature") on the named agent's live config.
	///     Agent settings never require a restart — they're read fresh on the next request.
	/// </summary>
	public UpdateResult UpdateAgentSetting(IReadOnlyDictionary<string, IAgentConfig> agents, string agentName, string fieldName, object? value)
	{
		if (!agents.TryGetValue(agentName, out IAgentConfig? agent))
		{
			return new UpdateResult(false, $"Unknown agent: {agentName}", requiresRestart: false);
		}

		AgentSettingDescriptor? descriptor = AgentSettingRegistry.Find(fieldName);
		if (descriptor is null)
		{
			return new UpdateResult(false, $"Unknown agent field: {fieldName}", requiresRestart: false);
		}

		try
		{
			descriptor.Apply(agent, value);
			return new UpdateResult(true, null, requiresRestart: false);
		}
		catch (Exception ex)
		{
			return new UpdateResult(false, ex.Message, requiresRestart: false);
		}
	}
}

/// <summary>
///     Result of updating a configuration setting.
/// </summary>
internal sealed class UpdateResult
{
	public bool Success { get; }
	public string? ErrorMessage { get; }
	public bool RequiresRestart { get; }

	public UpdateResult(bool success, string? errorMessage, bool requiresRestart)
	{
		Success = success;
		ErrorMessage = errorMessage;
		RequiresRestart = requiresRestart;
	}
}
