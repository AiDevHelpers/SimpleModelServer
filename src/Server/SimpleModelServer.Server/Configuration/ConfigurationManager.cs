namespace SimpleModelServer.Server.Configuration;

/// <summary>
///     Manages configuration validation, type checking, and hot-reload determination.
///     Backed entirely by <see cref="GlobalSettingRegistry" /> — the single source of truth for
///     which settings exist, their restart requirement, and how to validate them.
/// </summary>
internal sealed class ConfigurationManager : IConfigurationManager
{
	private static string SettingName(string settingPath)
		// settingPath can be "global.routerPort" or "routerPort"
		=> settingPath.Contains('.', StringComparison.Ordinal)
			? settingPath.Split('.')[^1]
			: settingPath;

	/// <summary>
	///     Determines if a configuration setting requires a full server restart.
	/// </summary>
	public bool RequiresRestart(string settingPath)
		=> GlobalSettingRegistry.Find(SettingName(settingPath))?.RequiresRestart ?? false;

	/// <summary>
	///     Determines if a configuration setting can be hot-reloaded.
	/// </summary>
	public bool CanHotReload(string settingPath)
	{
		SettingDescriptor? descriptor = GlobalSettingRegistry.Find(SettingName(settingPath));
		return descriptor is not null && !descriptor.RequiresRestart;
	}

	/// <summary>
	///     Validates a config value based on the setting path and type.
	/// </summary>
	public ValidationResult Validate(string settingPath, object? value)
	{
		string settingName = SettingName(settingPath);
		SettingDescriptor? descriptor = GlobalSettingRegistry.Find(settingName);
		return descriptor is null
			? ValidationResult.Invalid($"Unknown setting: {settingName}")
			: descriptor.Validate(value);
	}

	/// <summary>
	///     Validates a value for an agent config field (e.g. "systemPrompt", "temperature").
	/// </summary>
	public ValidationResult ValidateAgentField(string fieldName, object? value)
	{
		AgentSettingDescriptor? descriptor = AgentSettingRegistry.Find(fieldName);
		return descriptor is null
			? ValidationResult.Invalid($"Unknown agent field: {fieldName}")
			: descriptor.Validate(value);
	}
}

/// <summary>
///     Result of a configuration validation check.
/// </summary>
internal sealed class ValidationResult
{
	public bool IsValid { get; }
	public string? ErrorMessage { get; }

	private ValidationResult(bool isValid, string? errorMessage)
	{
		IsValid = isValid;
		ErrorMessage = errorMessage;
	}

	public static ValidationResult Valid() => new(true, null);
	public static ValidationResult Invalid(string errorMessage) => new(false, errorMessage);
}
