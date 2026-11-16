namespace SimpleModelServer.Contracts.Dtos;

/// <summary>
///     Complete system information response including capabilities and configuration.
/// </summary>
public class SystemInfoResponse
{
	/// <summary>System capabilities including models, agents, hardware, and features.</summary>
	public SystemCapabilities Capabilities { get; set; } = new();

	/// <summary>Current global and model configuration.</summary>
	public SystemConfiguration Configuration { get; set; } = new();

	/// <summary>Timestamp when this information was gathered (UTC).</summary>
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
///     Available system capabilities.
/// </summary>
public class SystemCapabilities
{
	/// <summary>Names of available models configured in the system.</summary>
	public string[] Models { get; set; } = [];

	/// <summary>Supported agent types (e.g., code, debug, planning, research).</summary>
	public string[] AgentTypes { get; set; } = [];

	/// <summary>Hardware capabilities and resource information.</summary>
	public HardwareCapabilities Hardware { get; set; } = new();

	/// <summary>Feature flags and capabilities enabled in this system.</summary>
	public string[] Features { get; set; } = [];
}

/// <summary>
///     Hardware capabilities and resource information.
/// </summary>
public class HardwareCapabilities
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
public class SystemConfiguration
{
	/// <summary>Global router and system settings.</summary>
	public GlobalConfigInfo Global { get; set; } = new();

	/// <summary>Model role assignments (e.g., draft, main, embed).</summary>
	public Dictionary<string, string> ModelRoles { get; set; } = new();

	/// <summary>Model source configurations.</summary>
	public Dictionary<string, ModelSourceInfo> ModelSources { get; set; } = new();

	/// <summary>Agent configurations.</summary>
	public Dictionary<string, AgentInfo> Agents { get; set; } = new();

	/// <summary>Configured MCP upstream servers aggregated behind the /mcp gateway endpoint.</summary>
	public Dictionary<string, McpServerInfo> McpServers { get; set; } = new();
}

/// <summary>
///     Status of one configured MCP upstream server (see /mcp gateway).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056",
	Justification = "Path is a plain string surfaced verbatim from config/JSON for display purposes; changing to System.Uri would complicate serialization for no functional benefit, and it doubles as a filesystem path for stdio upstreams.")]
public class McpServerInfo
{
	/// <summary>How the gateway connects to this upstream — "Http" or "Stdio".</summary>
	public string? TransportType { get; set; }

	/// <summary>The upstream server's HTTP endpoint URL, or its executable file path for a stdio upstream.</summary>
	public string? Path { get; set; }

	/// <summary>Whether the router successfully connected to this upstream at startup.</summary>
	public bool Connected { get; set; }

	/// <summary>How many of this upstream's tools were aggregated into the /mcp gateway.</summary>
	public int ToolCount { get; set; }
}

/// <summary>
///     Global configuration settings.
/// </summary>
public class GlobalConfigInfo
{
	/// <summary>Router host address (typically 127.0.0.1 or 0.0.0.0).</summary>
	public string? RouterHost { get; set; }

	/// <summary>Router HTTP port.</summary>
	public int RouterPort { get; set; }

	/// <summary>Virtual model name advertised by the router.</summary>
	public string? RouterModelName { get; set; }

	/// <summary>Ollama server port.</summary>
	public int OllamaPort { get; set; }

	/// <summary>Which inference backend serves generation requests: "ollama" or "lmstudio".</summary>
	public string? Provider { get; set; }

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
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056",
	Justification = "Url is a plain string surfaced verbatim from config/JSON for display purposes; changing to System.Uri would complicate serialization for no functional benefit.")]
public class ModelSourceInfo
{
	/// <summary>Source type (e.g., ollama, huggingface, local).</summary>
	public string? Type { get; set; }

	/// <summary>Provider URL or path.</summary>
	public string? Url { get; set; }

	/// <summary>Source-specific settings.</summary>
	public Dictionary<string, object?> Settings { get; set; } = new();
}

/// <summary>
///     Agent configuration information.
/// </summary>
public class AgentInfo
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
}
