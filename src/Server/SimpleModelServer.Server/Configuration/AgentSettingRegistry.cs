using System.Globalization;
using SimpleModelServer.Contracts.Agents;

namespace SimpleModelServer.Server.Configuration;

/// <summary>
///     Describes one admin-updatable <see cref="IAgentConfig" /> field: its validation rule and
///     how to apply a value to the live agent config. Mirrors <see cref="GlobalSettingRegistry" />
///     for the "agents.&lt;name&gt;.&lt;field&gt;" setting-path family.
/// </summary>
internal sealed record AgentSettingDescriptor(
	string CanonicalName,
	Func<object?, ValidationResult> Validate,
	Action<IAgentConfig, object?> Apply);

internal static class AgentSettingRegistry
{
	internal static readonly IReadOnlyList<AgentSettingDescriptor> All =
	[
		new("systemPrompt", ValidateNonEmptyString, (a, v) => a.SystemPrompt = v?.ToString() ?? ""),
		new("temperature", ValidateTemperature, (a, v) => { if (TryParseDouble(v, out double t)) a.Temperature = t; }),
	];

	internal static AgentSettingDescriptor? Find(string fieldName)
	{
		foreach (AgentSettingDescriptor descriptor in All)
		{
			if (string.Equals(descriptor.CanonicalName, fieldName, StringComparison.OrdinalIgnoreCase))
			{
				return descriptor;
			}
		}

		return null;
	}

	private static ValidationResult ValidateNonEmptyString(object? value)
	{
		if (value is null)
		{
			return ValidationResult.Invalid("String cannot be null");
		}

		string? str = value.ToString();
		return string.IsNullOrWhiteSpace(str)
			? ValidationResult.Invalid("String cannot be empty")
			: ValidationResult.Valid();
	}

	private static ValidationResult ValidateTemperature(object? value)
	{
		if (value is null)
		{
			return ValidationResult.Invalid("Temperature cannot be null");
		}

		return TryParseDouble(value, out double d) && d >= 0 && d <= 2
			? ValidationResult.Valid()
			: ValidationResult.Invalid($"Temperature must be a number between 0 and 2, got: {value}");
	}

	private static bool TryParseDouble(object? value, out double result)
	{
		switch (value)
		{
			case double d:
				result = d;
				return true;
			case int i:
				result = i;
				return true;
			default:
				return double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
		}
	}
}
