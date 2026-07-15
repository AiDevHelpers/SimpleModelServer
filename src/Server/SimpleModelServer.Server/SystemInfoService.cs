using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Dtos;
using SimpleModelServer.Hardware;
using SimpleModelServer.Hardware.Interfaces;

namespace SimpleModelServer.Server;

/// <summary>
///     Provides system information including capabilities and current configuration.
///     Used by the GET /api/system/info endpoint to expose system state to clients.
/// </summary>
internal sealed class SystemInfoService
{
    private readonly IAppConfig config;
    private readonly IHardwareDetection hardwareDetection;
    private readonly McpUpstreamRegistry mcpUpstreamRegistry;

    public SystemInfoService(IAppConfig config, IHardwareDetection hardwareDetection, McpUpstreamRegistry mcpUpstreamRegistry)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.hardwareDetection = hardwareDetection ?? throw new ArgumentNullException(nameof(hardwareDetection));
        this.mcpUpstreamRegistry = mcpUpstreamRegistry ?? throw new ArgumentNullException(nameof(mcpUpstreamRegistry));
    }

    /// <summary>
    ///     Gets complete system information including capabilities and configuration.
    /// </summary>
    public SystemInfoResponse GetSystemInfo()
    {
        ResolvedHardwareSettings hardware = hardwareDetection.Resolve(config.Global);

        return new SystemInfoResponse
        {
            Capabilities = BuildCapabilities(hardware),
            Configuration = BuildConfiguration(hardware),
            Timestamp = DateTime.UtcNow
        };
    }

    private SystemCapabilities BuildCapabilities(ResolvedHardwareSettings hardware)
    {
        // Collect all known model names
        var modelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(config.Global.DraftModel))
        {
            modelNames.Add(config.Global.DraftModel);
        }

        if (!string.IsNullOrWhiteSpace(config.Global.MainModel))
        {
            modelNames.Add(config.Global.MainModel);
        }

        string embedModel = config.ResolveEmbedModelName();
        if (!string.IsNullOrWhiteSpace(embedModel))
        {
            modelNames.Add(embedModel);
        }

        // Add model sources
        foreach (string modelName in config.ModelSources.Keys)
        {
            modelNames.Add(modelName);
        }

        // Collect agent types
        var agentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "code",
            "debug",
            "planning",
            "research"
        };
        foreach (string agentType in config.Agents.Keys)
        {
            agentTypes.Add(agentType);
        }

        // Determine enabled features
        var features = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.Global.DraftModel) && !string.IsNullOrWhiteSpace(config.Global.MainModel))
        {
            features.Add("draft-and-refine");
        }

        if (config.Agents.Count > 0)
        {
            features.Add("multi-agent");
        }

        if (!string.IsNullOrWhiteSpace(embedModel))
        {
            features.Add("embeddings");
        }

        if (!string.IsNullOrWhiteSpace(config.Global.AgentType) ||
            !string.IsNullOrWhiteSpace(config.Global.DefaultAgentType))
        {
            features.Add("agent-selection");
        }

        return new SystemCapabilities
        {
            Models = modelNames.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToArray(),
            AgentTypes = agentTypes.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray(),
            Hardware = BuildHardwareCapabilities(hardware),
            Features = features.OrderBy(f => f).ToArray()
        };
    }

    private HardwareCapabilities BuildHardwareCapabilities(ResolvedHardwareSettings hardware)
    {
        return new HardwareCapabilities
        {
            GpuAvailable = hardware.Gpu.Detected,
            GpuName = hardware.Gpu.Name,
            GpuMemoryMb = hardware.Gpu.MemoryMb,
            GpuFreeMemoryMb = hardware.Gpu.FreeMemoryMb,
            GpuLayers = hardware.GpuLayers,
            CpuThreads = hardware.CpuThreads,
            MaxConcurrentSummaryRequests = hardware.MaxConcurrentSummaryRequests
        };
    }

    private SystemConfiguration BuildConfiguration(ResolvedHardwareSettings hardware)
    {
        return new SystemConfiguration
        {
            Global = BuildGlobalConfig(),
            ModelRoles = new Dictionary<string, string>(config.ModelRoles, StringComparer.OrdinalIgnoreCase),
            ModelSources = BuildModelSources(),
            Agents = BuildAgentInfo(),
            McpServers = BuildMcpServers()
        };
    }

    private Dictionary<string, McpServerInfo> BuildMcpServers()
    {
        var result = new Dictionary<string, McpServerInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, McpUpstreamStatus> kvp in mcpUpstreamRegistry.GetStatus())
        {
            result[kvp.Key] = new McpServerInfo
            {
                TransportType = kvp.Value.TransportType.ToString(),
                Path = kvp.Value.Path,
                Connected = kvp.Value.Connected,
                ToolCount = kvp.Value.ToolCount
            };
        }

        return result;
    }

    private GlobalConfigInfo BuildGlobalConfig()
    {
        return new GlobalConfigInfo
        {
            RouterHost = config.Global.RouterHost,
            RouterPort = config.Global.RouterPort,
            RouterModelName = config.Global.RouterModelName,
            OllamaPort = config.Global.OllamaPort,
            Provider = config.Global.Provider.Type.ToString().ToLowerInvariant(),
            DraftModel = config.Global.DraftModel,
            MainModel = config.Global.MainModel,
            EmbedModel = !string.IsNullOrWhiteSpace(config.Global.EmbedModel)
                ? config.Global.EmbedModel
                : null,
            ContextSize = config.Global.ContextSize,
            DefaultAgentType = !string.IsNullOrWhiteSpace(config.Global.DefaultAgentType)
                ? config.Global.DefaultAgentType
                : null,
            ForcedAgentType = !string.IsNullOrWhiteSpace(config.Global.AgentType)
                ? config.Global.AgentType
                : null,  // AgentType forces a specific agent (if set, takes precedence over DefaultAgentType)
            MdnsEnabled = config.Global.MdnsEnabled,
            MdnsServiceName = !string.IsNullOrWhiteSpace(config.Global.MdnsServiceName)
                ? config.Global.MdnsServiceName
                : null
        };
    }

    private Dictionary<string, ModelSourceInfo> BuildModelSources()
    {
        var result = new Dictionary<string, ModelSourceInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in config.ModelSources)
        {
            var modelSourceConfig = kvp.Value;
            result[kvp.Key] = new ModelSourceInfo
            {
                Type = null,  // Not available in ModelSourceConfig
                Url = null,   // Not available in ModelSourceConfig
                Settings = new Dictionary<string, object?>
                {
                    { "base", modelSourceConfig.Base },
                    { "numCtx", modelSourceConfig.NumCtx },
                    { "keepAlive", modelSourceConfig.KeepAlive },
                    { "numGpu", modelSourceConfig.NumGpu },
                    { "stop", modelSourceConfig.Stop },
                    { "family", modelSourceConfig.Family },
                    { "variant", modelSourceConfig.Variant },
                    { "size", modelSourceConfig.Size },
                    { "chatSuffix", modelSourceConfig.ChatSuffix }
                }
            };
        }

        return result;
    }

    private Dictionary<string, AgentInfo> BuildAgentInfo()
    {
        var result = new Dictionary<string, AgentInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in config.Agents)
        {
            var agentConfig = kvp.Value;
            result[kvp.Key] = new AgentInfo
            {
                Name = kvp.Key,  // Use the key as the name
                Description = null,  // Not available in AgentConfig
                DraftModel = null,  // Not available in AgentConfig
                MainModel = null,  // Not available in AgentConfig
                Settings = new Dictionary<string, object?>
                {
                    { "systemPrompt", agentConfig.SystemPrompt },
                    { "temperature", agentConfig.Temperature },
                    { "sampling", agentConfig.Sampling }
                }
            };
        }

        return result;
    }
}
