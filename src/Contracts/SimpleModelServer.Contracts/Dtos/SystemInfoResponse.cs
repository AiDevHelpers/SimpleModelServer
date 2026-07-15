namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Complete system information response including capabilities and configuration.
/// </summary>
public class SystemInfoResponse : ISystemInfoResponse
{
    /// <summary>System capabilities including models, agents, hardware, and features.</summary>
    public SystemCapabilities Capabilities { get; set; } = new();

    ISystemCapabilities ISystemInfoResponse.Capabilities => Capabilities;

    /// <summary>Current global and model configuration.</summary>
    public SystemConfiguration Configuration { get; set; } = new();

    ISystemConfiguration ISystemInfoResponse.Configuration => Configuration;

    /// <summary>Timestamp when this information was gathered (UTC).</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
///     Available system capabilities.
/// </summary>
public class SystemCapabilities : ISystemCapabilities
{
    /// <summary>Names of available models configured in the system.</summary>
    public string[] Models { get; set; } = [];

    /// <summary>Supported agent types (e.g., code, debug, planning, research).</summary>
    public string[] AgentTypes { get; set; } = [];

    /// <summary>Hardware capabilities and resource information.</summary>
    public HardwareCapabilities Hardware { get; set; } = new();

    IHardwareCapabilities ISystemCapabilities.Hardware => Hardware;

    /// <summary>Feature flags and capabilities enabled in this system.</summary>
    public string[] Features { get; set; } = [];
}

/// <summary>
///     Hardware capabilities and resource information.
/// </summary>
public class HardwareCapabilities : IHardwareCapabilities
{
    /// <summary>Whether a GPU is detected and available.</summary>
    public bool GpuAvailable { get; set; }

    /// <summary>GPU name/model if available (e.g., "NVIDIA GeForce RTX 4090").</summary>
    public string? GpuName { get; set; }

    /// <summary>Total GPU memory in MB, if available.</summary>
    public int? GpuMemoryMb { get; set; }

    /// <summary>Free GPU memory in MB at detection time, if available.</summary>
    public int? GpuFreeMemoryMb { get; set; }

    /// <summary>GPU layers allocated for inference.</summary>
    public int? GpuLayers { get; set; }

    /// <summary>CPU thread count available.</summary>
    public int? CpuThreads { get; set; }

    /// <summary>Maximum concurrent background inference requests this machine can handle.</summary>
    public int MaxConcurrentSummaryRequests { get; set; }
}

/// <summary>
///     Current system configuration.
/// </summary>
public class SystemConfiguration : ISystemConfiguration
{
    /// <summary>Global router and system settings.</summary>
    public GlobalConfigInfo Global { get; set; } = new();

    IGlobalConfigInfo ISystemConfiguration.Global => Global;

    /// <summary>Model role assignments (e.g., draft, main, embed).</summary>
    public Dictionary<string, string> ModelRoles { get; set; } = new();

    IReadOnlyDictionary<string, string> ISystemConfiguration.ModelRoles => ModelRoles;

    /// <summary>Model source configurations.</summary>
    public Dictionary<string, ModelSourceInfo> ModelSources { get; set; } = new();

    IReadOnlyDictionary<string, IModelSourceInfo> ISystemConfiguration.ModelSources =>
        ModelSources.ToDictionary(kvp => kvp.Key, kvp => (IModelSourceInfo)kvp.Value);

    /// <summary>Agent configurations.</summary>
    public Dictionary<string, AgentInfo> Agents { get; set; } = new();

    IReadOnlyDictionary<string, IAgentInfo> ISystemConfiguration.Agents =>
        Agents.ToDictionary(kvp => kvp.Key, kvp => (IAgentInfo)kvp.Value);
}

/// <summary>
///     Global configuration settings.
/// </summary>
public class GlobalConfigInfo : IGlobalConfigInfo
{
    /// <summary>Router host address (typically 127.0.0.1 or 0.0.0.0).</summary>
    public string? RouterHost { get; set; }

    /// <summary>Router HTTP port.</summary>
    public int RouterPort { get; set; }

    /// <summary>Virtual model name advertised by the router.</summary>
    public string? RouterModelName { get; set; }

    /// <summary>Ollama server port.</summary>
    public int OllamaPort { get; set; }

    /// <summary>Draft model name.</summary>
    public string? DraftModel { get; set; }

    /// <summary>Main (refine) model name.</summary>
    public string? MainModel { get; set; }

    /// <summary>Embedding model name.</summary>
    public string? EmbedModel { get; set; }

    /// <summary>Default context size (num_ctx) if configured.</summary>
    public int? ContextSize { get; set; }

    /// <summary>Default agent type (code, debug, planning, research).</summary>
    public string? DefaultAgentType { get; set; }

    /// <summary>Forced agent type, if configured.</summary>
    public string? ForcedAgentType { get; set; }

    /// <summary>Whether mDNS service discovery is enabled.</summary>
    public bool MdnsEnabled { get; set; }

    /// <summary>mDNS service name override, if configured.</summary>
    public string? MdnsServiceName { get; set; }
}

/// <summary>
///     Model source configuration information.
/// </summary>
public class ModelSourceInfo : IModelSourceInfo
{
    /// <summary>Source type (e.g., ollama, huggingface, local).</summary>
    public string? Type { get; set; }

    /// <summary>Provider URL or path.</summary>
    public string? Url { get; set; }

    /// <summary>Source-specific settings.</summary>
    public Dictionary<string, object?> Settings { get; set; } = new();

    IReadOnlyDictionary<string, object?> IModelSourceInfo.Settings => Settings;
}

/// <summary>
///     Agent configuration information.
/// </summary>
public class AgentInfo : IAgentInfo
{
    /// <summary>Agent display name.</summary>
    public string? Name { get; set; }

    /// <summary>Agent description.</summary>
    public string? Description { get; set; }

    /// <summary>Draft model override for this agent, if configured.</summary>
    public string? DraftModel { get; set; }

    /// <summary>Main model override for this agent, if configured.</summary>
    public string? MainModel { get; set; }

    /// <summary>Agent-specific settings.</summary>
    public Dictionary<string, object?> Settings { get; set; } = new();

    IReadOnlyDictionary<string, object?> IAgentInfo.Settings => Settings;
}
