namespace SimpleModelServer.Server.Configuration;

/// <summary>
///     Manages configuration validation and determines restart requirements.
/// </summary>
internal interface IConfigurationManager
{
	/// <summary>
	///     Determines if a configuration setting requires a server restart.
	/// </summary>
	bool RequiresRestart(string settingPath);

	/// <summary>
	///     Determines if a configuration setting can be hot-reloaded.
	/// </summary>
	bool CanHotReload(string settingPath);

	/// <summary>
	///     Validates a config value based on the setting path and type.
	/// </summary>
	ValidationResult Validate(string settingPath, object? value);

	/// <summary>
	///     Validates a value for an agent config field (e.g. "systemPrompt", "temperature").
	/// </summary>
	ValidationResult ValidateAgentField(string fieldName, object? value);
}
