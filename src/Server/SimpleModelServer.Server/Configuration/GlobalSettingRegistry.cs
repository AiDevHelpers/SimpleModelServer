using SimpleModelServer.Config.Configuration;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server.Configuration;

/// <summary>
///     Describes one admin-updatable <see cref="IGlobalConfig" /> setting: its accepted names,
///     restart requirement, validation rule, and how to apply a value to the live config.
///     This is the single source of truth for the admin config-update API — adding a new
///     updatable setting means adding one entry here, not editing four independent lists across
///     <see cref="ConfigurationManager" /> and <see cref="ConfigurationUpdater" />.
/// </summary>
internal sealed record SettingDescriptor(
    string CanonicalName,
    string SnakeCaseAlias,
    bool RequiresRestart,
    Func<object?, ValidationResult> Validate,
    Action<IGlobalConfig, object?> Apply);

internal static class GlobalSettingRegistry
{
    internal static readonly IReadOnlyList<SettingDescriptor> All =
    [
        new("defaultAgentType", "default_agent_type", RequiresRestart: false,
            ValidateOptionalString, (c, v) => c.DefaultAgentType = v?.ToString() ?? ""),

        new("agentType", "agent_type", RequiresRestart: false,
            ValidateOptionalString, (c, v) => c.AgentType = v?.ToString() ?? ""),

        new("routerPort", "router_port", RequiresRestart: true,
            ValidatePort, (c, v) => { if (TryParseInt(v, out int p)) c.RouterPort = p; }),

        new("ollamaPort", "ollama_port", RequiresRestart: true,
            ValidatePort, (c, v) => { if (TryParseInt(v, out int p)) c.OllamaPort = p; }),

        new("routerHost", "router_host", RequiresRestart: true,
            ValidateNonEmptyString, (c, v) => c.RouterHost = v?.ToString() ?? "localhost"),

        new("draftModel", "draft_model", RequiresRestart: true,
            ValidateNonEmptyString, (c, v) => c.DraftModel = v?.ToString() ?? ""),

        new("mainModel", "main_model", RequiresRestart: true,
            ValidateNonEmptyString, (c, v) => c.MainModel = v?.ToString() ?? ""),

        new("embedModel", "embed_model", RequiresRestart: false,
            ValidateOptionalString, (c, v) => c.EmbedModel = v?.ToString() ?? ""),

        new("routerModelName", "router_model_name", RequiresRestart: false,
            ValidateNonEmptyString, (c, v) => c.RouterModelName = v?.ToString() ?? "HelperModel"),

        new("contextSize", "context_size", RequiresRestart: false,
            ValidateNonNegativeInt, (c, v) => { if (TryParseInt(v, out int cs)) c.ContextSize = cs; }),

        new("draftHandoffTimeoutMs", "draft_handoff_timeout_ms", RequiresRestart: false,
            ValidateNonNegativeInt, (c, v) => { if (TryParseInt(v, out int t)) c.DraftHandoffTimeoutMs = t; }),

        new("draftMode", "draft_mode", RequiresRestart: false,
            ValidateOptionalString, (c, v) => c.DraftMode = ParseDraftMode(v?.ToString())),

        new("mdnsEnabled", "mdns_enabled", RequiresRestart: false,
            ValidateBool, (c, v) => { if (bool.TryParse(v?.ToString(), out bool b)) c.MdnsEnabled = b; }),

        new("mdnsServiceName", "mdns_service_name", RequiresRestart: false,
            ValidateOptionalString, (c, v) => c.MdnsServiceName = v?.ToString() ?? ""),

        new("trustedNetworks", "trusted_networks", RequiresRestart: false,
            ValidateStringArray, (c, v) => c.TrustedNetworks = ParseStringArray(v)),

        new("trustedApiKeys", "trusted_api_keys", RequiresRestart: false,
            ValidateStringArray, (c, v) => c.TrustedApiKeys = ParseStringArray(v)),

        new("gpuLayers", "gpu_layers", RequiresRestart: false,
            ValidateHardwareSetting, (c, v) => c.GpuLayers = ParseHardwareSetting(v)),

        new("cpuThreads", "cpu_threads", RequiresRestart: false,
            ValidateHardwareSetting, (c, v) => c.CpuThreads = ParseHardwareSetting(v)),
    ];

    /// <summary>
    ///     Looks up a descriptor by canonical camelCase name or its snake_case alias
    ///     (case-insensitive), the two forms admin API callers may send.
    /// </summary>
    internal static SettingDescriptor? Find(string settingName)
    {
        foreach (SettingDescriptor descriptor in All)
        {
            if (string.Equals(descriptor.CanonicalName, settingName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(descriptor.SnakeCaseAlias, settingName, StringComparison.OrdinalIgnoreCase))
            {
                return descriptor;
            }
        }

        return null;
    }

    // ── Validators ──

    private static ValidationResult ValidatePort(object? value)
    {
        if (value is null)
        {
            return ValidationResult.Invalid("Port cannot be null");
        }

        if (value is int intPort && intPort > 0 && intPort <= 65535)
        {
            return ValidationResult.Valid();
        }

        if (int.TryParse(value.ToString(), out int port) && port > 0 && port <= 65535)
        {
            return ValidationResult.Valid();
        }

        return ValidationResult.Invalid($"Port must be a number between 1 and 65535, got: {value}");
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

    private static ValidationResult ValidateOptionalString(object? value)
    {
        return value is null || value is string
            ? ValidationResult.Valid()
            : ValidationResult.Invalid($"Expected string, got: {value.GetType().Name}");
    }

    private static ValidationResult ValidateStringArray(object? value)
    {
        return value switch
        {
            null => ValidationResult.Valid(), // Arrays are optional
            JsonArray => ValidationResult.Valid(),
            string[] => ValidationResult.Valid(),
            IEnumerable<string> => ValidationResult.Valid(),
            _ => ValidationResult.Invalid($"Expected string array, got: {value.GetType().Name}")
        };
    }

    private static ValidationResult ValidateNonNegativeInt(object? value)
    {
        if (value is null)
        {
            return ValidationResult.Valid(); // Optional
        }

        if (value is int intVal && intVal >= 0)
        {
            return ValidationResult.Valid();
        }

        if (int.TryParse(value.ToString(), out int parsed) && parsed >= 0)
        {
            return ValidationResult.Valid();
        }

        return ValidationResult.Invalid($"Expected non-negative integer, got: {value}");
    }

    private static ValidationResult ValidateBool(object? value)
    {
        if (value is null)
        {
            return ValidationResult.Invalid("Boolean cannot be null");
        }

        if (value is bool)
        {
            return ValidationResult.Valid();
        }

        if (bool.TryParse(value.ToString(), out _))
        {
            return ValidationResult.Valid();
        }

        return ValidationResult.Invalid($"Expected boolean, got: {value}");
    }

    /// <summary>
    ///     GPU layer / CPU thread settings accept "auto" or a non-negative integer (see
    ///     <see cref="ParseHardwareSetting" />) — unlike the plain integer settings above, a bare
    ///     <see cref="ValidateNonNegativeInt" /> would incorrectly reject "auto".
    /// </summary>
    private static ValidationResult ValidateHardwareSetting(object? value)
    {
        if (value is null)
        {
            return ValidationResult.Valid(); // Optional
        }

        if (value is string str)
        {
            return str.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                   (int.TryParse(str, out int parsed) && parsed >= 0)
                ? ValidationResult.Valid()
                : ValidationResult.Invalid($"Expected \"auto\" or a non-negative integer, got: {value}");
        }

        if (value is int intVal && intVal >= 0)
        {
            return ValidationResult.Valid();
        }

        return ValidationResult.Invalid($"Expected \"auto\" or a non-negative integer, got: {value}");
    }

    // ── Value parsing (shared by Apply delegates above) ──

    private static bool TryParseInt(object? value, out int result)
    {
        if (value is int i)
        {
            result = i;
            return true;
        }

        return int.TryParse(value?.ToString(), out result);
    }

    private static DraftMode ParseDraftMode(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "CLEANUP" => DraftMode.Cleanup,
            "BOTH" => DraftMode.Both,
            _ => DraftMode.Preview
        };
    }

    private static string[] ParseStringArray(object? value)
    {
        if (value is null)
        {
            return [];
        }

        if (value is JsonArray arr)
        {
            List<string> result = [];
            foreach (JsonNode? item in arr)
            {
                string? s = item?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    result.Add(s);
                }
            }

            return result.ToArray();
        }

        if (value is string[] arr2)
        {
            return arr2;
        }

        if (value is IEnumerable<string> enumerable)
        {
            return enumerable.ToArray();
        }

        return [];
    }

    private static HardwareSetting ParseHardwareSetting(object? value)
    {
        if (value is null)
        {
            return HardwareSetting.Unspecified;
        }

        if (value is string str)
        {
            if (str.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return HardwareSetting.Automatic;
            }

            return int.TryParse(str, out int intVal) && intVal >= 0
                ? HardwareSetting.Override(intVal)
                : HardwareSetting.Unspecified;
        }

        if (value is int intVal2 && intVal2 >= 0)
        {
            return HardwareSetting.Override(intVal2);
        }

        return HardwareSetting.Unspecified;
    }
}
