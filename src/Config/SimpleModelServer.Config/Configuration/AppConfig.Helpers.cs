using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed partial class AppConfig
{
	private static JsonObject? GetObject(JsonObject source, params string[] names)
	{
		return GetNode(source, names) as JsonObject;
	}

	private static JsonNode? GetNode(JsonObject source, params string[] names)
	{
		foreach (string name in names)
		{
			foreach (KeyValuePair<string, JsonNode?> pair in source)
			{
				if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
				{
					return pair.Value;
				}
			}
		}

		return null;
	}

	private static string? GetString(JsonObject source, params string[] names)
	{
		return GetNode(source, names)?.GetValue<string>();
	}

	private static double? GetDouble(JsonObject source, params string[] names)
	{
		JsonNode? node = GetNode(source, names);
		return node?.GetValue<double>();
	}

	private static bool? GetBool(JsonObject source, params string[] names)
	{
		JsonNode? node = GetNode(source, names);
		return node is JsonValue value && value.TryGetValue(out bool b) ? b : null;
	}

	private static int? GetInt(JsonObject source, params string[] names)
	{
		JsonNode? node = GetNode(source, names);
		return node?.GetValue<int>();
	}

	private static HardwareSetting GetHardwareSetting(JsonObject source, params string[] names)
	{
		JsonNode? node = GetNode(source, names);
		if (node is null)
		{
			return HardwareSetting.Unspecified;
		}

		if (node is JsonValue value)
		{
			if (value.TryGetValue<int>(out int intValue))
			{
				return HardwareSetting.Override(intValue);
			}

			if (value.TryGetValue<string>(out string? stringValue) &&
				string.Equals(stringValue, "auto", StringComparison.OrdinalIgnoreCase))
			{
				return HardwareSetting.Automatic;
			}
		}

		throw new InvalidOperationException($"{names[0]} must be an integer or \"auto\".");
	}
}
