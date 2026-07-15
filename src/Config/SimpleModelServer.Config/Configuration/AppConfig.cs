using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed class AppConfig : IAppConfig
{
    public IGlobalConfig Global { get; init; } = new GlobalConfig();
    public Dictionary<string, IAgentConfig> Agents { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IModelSourceConfig> ModelSources { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ModelRoles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    IReadOnlyDictionary<string, IAgentConfig> IAppConfig.Agents => Agents;
    IReadOnlyDictionary<string, IModelSourceConfig> IAppConfig.ModelSources => ModelSources;
    IReadOnlyDictionary<string, string> IAppConfig.ModelRoles => ModelRoles;

    public string DefaultAgentType => string.IsNullOrWhiteSpace(Global.DefaultAgentType)
        ? "ask"
        : Global.DefaultAgentType;

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        JsonObject json = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                   ?? throw new InvalidOperationException($"Config file must contain a JSON object: {path}");

        GlobalConfig global = ParseGlobal(GetObject(json, "global") ?? []);

        // Prefer a "models" section in the primary config file; fall back to the legacy
        // separate file pointed to by modelSourcesPath for backward compatibility.
        JsonObject? inlineModels = GetObject(json, "models");
        (Dictionary<string, IModelSourceConfig>? modelSources, Dictionary<string, string>? modelRoles) = inlineModels is not null
            ? ParseModelSources(inlineModels)
            : LoadModelSourcesFromFile(path, global.ModelSourcesPath);

        return new AppConfig
        {
            Global = global,
            Agents = ParseAgents(GetObject(json, "agents") ?? GetObject(json, "modes") ?? []),
            ModelSources = modelSources,
            ModelRoles = modelRoles
        };
    }

    public IAgentConfig Agent(string agentType)
    {
        return Agents.TryGetValue(agentType, out IAgentConfig? settings) ? settings : new SimpleModelServer.Contracts.Dtos.Agents.AgentConfig();
    }

    /// <summary>
    ///     Resolves the embedding model's name: <see cref="GlobalConfig.EmbedModel" /> override,
    ///     then the 'embed' model role, then the 'nomic-embed-text' default. Mirrors
    ///     SimpleModelPairGenerationService.EmbedModelName — kept here too so callers that only have an
    ///     AppConfig (no SimpleModelPairGenerationService instance), like OllamaProcessManager and
    ///     ModelHealthMonitor, can identify the embed model without a circular dependency.
    /// </summary>
    public string ResolveEmbedModelName()
    {
        return !string.IsNullOrWhiteSpace(Global.EmbedModel)
            ? Global.EmbedModel
            : ModelRoles.TryGetValue("embed", out string? e)
                ? e
                : "nomic-embed-text";
    }

    /// <summary>
    ///     True if <paramref name="model" /> is the configured embedding model. Embedding-only
    ///     models don't support Ollama's <c>/api/generate</c> endpoint — callers that ping/warm/
    ///     unload a model by name must route to <c>/api/embed</c> instead when this is true.
    /// </summary>
    public bool IsEmbedModel(string model)
    {
        return string.Equals(model, ResolveEmbedModelName(), StringComparison.OrdinalIgnoreCase);
    }

    public bool IsKnownAgentType(string agentType)
    {
        return Agents.ContainsKey(agentType) ||
               agentType.Equals("ask", StringComparison.OrdinalIgnoreCase) ||
               agentType.Equals("debug", StringComparison.OrdinalIgnoreCase) ||
               agentType.Equals("code", StringComparison.OrdinalIgnoreCase) ||
               agentType.Equals("planning", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parse models directly from an inline "models" JsonObject.</summary>
    private static (Dictionary<string, IModelSourceConfig> sources, Dictionary<string, string> roles) ParseModelSources(
        JsonObject modelsNode)
    {
        Dictionary<string, IModelSourceConfig> sources = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> roles = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, JsonNode?> pair in modelsNode)
        {
            if (pair.Value is not JsonObject modelObj)
            {
                continue;
            }

            // Try to get explicit name first, fall back to auto-generated from family/variant/size
            string? explicitName = GetString(modelObj, "name");
            string? family = GetString(modelObj, "family");
            string? variant = GetString(modelObj, "variant");
            string? size = GetString(modelObj, "size");
            string? chatSuffix = GetString(modelObj, "chatSuffix", "chat_suffix") ?? "-chat";

            string name = explicitName ?? GenerateModelName(family, variant, size, chatSuffix);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            // Try to get explicit base first, fall back to auto-generated from family/variant/size
            string? explicitBase = GetString(modelObj, "base");
            string base_ = explicitBase ?? GenerateModelBase(family, variant, size, chatSuffix);

            sources[name] = new ModelSourceConfig
            {
                Family = family,
                Variant = variant,
                Size = size,
                ChatSuffix = chatSuffix,
                Base = base_,
                NumCtx = GetInt(modelObj, "num_ctx", "numCtx"),
                KeepAlive = GetString(modelObj, "keep_alive", "keepAlive"),
                // Explicit pin: when set, GpuBudgetPlanner leaves this model out of auto VRAM
                // planning entirely and this value is sent as-is on every generate/warmup call.
                NumGpu = GetInt(modelObj, "num_gpu", "numGpu"),
                Stop = ParseStopTokens(GetNode(modelObj, "stop"))
            };

            roles[pair.Key] = name;
        }

        return (sources, roles);
    }

    /// <summary>
    ///     Legacy: load models from a separate file (config.json). Used when the primary
    ///     config has no inline "models" section.
    /// </summary>
    private static (Dictionary<string, IModelSourceConfig> sources, Dictionary<string, string> roles)
        LoadModelSourcesFromFile(string primaryConfigPath, string modelSourcesPath)
    {
        (Dictionary<string, IModelSourceConfig>, Dictionary<string, string>) empty = (new Dictionary<string, IModelSourceConfig>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(modelSourcesPath))
        {
            return empty;
        }

        string baseDir = Path.GetDirectoryName(Path.GetFullPath(primaryConfigPath)) ?? Directory.GetCurrentDirectory();
        string fullPath = Path.IsPathRooted(modelSourcesPath)
            ? modelSourcesPath
            : Path.Combine(baseDir, modelSourcesPath);

        if (!File.Exists(fullPath))
        {
            return empty;
        }

        try
        {
            JsonObject? json = JsonNode.Parse(File.ReadAllText(fullPath))?.AsObject();
            JsonObject? modelsNode = json is not null ? GetObject(json, "models") : null;
            return modelsNode is not null ? ParseModelSources(modelsNode) : empty;
        }
        catch
        {
            return empty;
        }
    }

    /// <summary>
    ///     Generates a model name from family, variant, and size components.
    ///     Format: "{family}-{variant}-{size}{chatSuffix}" (e.g., "qwen2.5-coder-0.5b-chat").
    ///     Returns empty string if any component is missing/empty.
    /// </summary>
    private static string GenerateModelName(string? family, string? variant, string? size, string? chatSuffix)
    {
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(variant) || string.IsNullOrWhiteSpace(size))
        {
            return "";
        }

        return $"{family}-{variant}-{size}{chatSuffix}";
    }

    /// <summary>
    ///     Generates a model base (Ollama reference) from family, variant, and size components.
    ///     Format: "{family}-{variant}{chatSuffix}:{size}" (e.g., "qwen2.5-coder-chat:0.5b").
    ///     Returns empty string if any component is missing/empty.
    /// </summary>
    private static string GenerateModelBase(string? family, string? variant, string? size, string? chatSuffix)
    {
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(variant) || string.IsNullOrWhiteSpace(size))
        {
            return "";
        }

        return $"{family}-{variant}{chatSuffix}:{size}";
    }

    private static string[] ParseStopTokens(JsonNode? node)
    {
        if (node is not JsonArray arr)
        {
            return [];
        }

        List<string> result = [];
        foreach (JsonNode? item in arr)
        {
            string? s = item?.GetValue<string>();
            if (!string.IsNullOrEmpty(s))
            {
                result.Add(s);
            }
        }

        return result.ToArray();
    }

    private static Dictionary<string, IAgentConfig> ParseAgents(JsonObject agents)
    {
        Dictionary<string, IAgentConfig> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, JsonNode?> pair in agents)
        {
            if (pair.Value is JsonObject agent)
            {
                result[pair.Key] = new SimpleModelServer.Contracts.Dtos.Agents.AgentConfig
                {
                    SystemPrompt = GetString(agent, "systemPrompt", "system_prompt", "system") ?? "",
                    Temperature = GetDouble(agent, "temperature", "tempature"),
                    Sampling = GetNode(agent, "sampling")?.DeepClone()
                };
            }
        }

        return result;
    }

    private static GlobalConfig ParseGlobal(JsonObject global)
    {
        return new GlobalConfig
        {
            DefaultAgentType =
                GetString(global, "defaultAgentType", "default_agent_type", "defaultAgent", "default_agent") ?? "code",
            GpuLayers = GetHardwareSetting(global, "gpuLayers", "gpu_layers", "numGpu", "num_gpu"),
            CpuThreads = GetHardwareSetting(global, "cpuThreads", "cpu_threads", "numThread", "num_thread"),
            DraftModel = GetString(global, "draftModel", "draft_model") ?? "",
            MainModel = GetString(global, "mainModel", "main_model") ?? "",
            EmbedModel = GetString(global, "embedModel", "embed_model") ?? "",
            OllamaPort = GetInt(global, "ollamaPort", "ollama_port") ?? 11434,
            RouterPort = GetInt(global, "routerPort", "router_port") ?? 11435,
            RouterModelName = GetString(global, "routerModelName", "router_model_name") ?? "HelperModel",
            ContextSize = GetInt(global, "contextSize", "context_size"),
            AgentType = GetString(global, "agentType", "agent_type") ?? "",
            RouterHost = GetString(global, "routerHost", "router_host") ?? "localhost",
            DraftHandoffTimeoutMs = GetInt(global, "draftHandoffTimeoutMs", "draft_handoff_timeout_ms") ?? 900,
            DraftMode = ParseDraftMode(GetString(global, "draftMode", "draft_mode")),
            // Kept for backward compat — ignored when primary config has inline "models" section
            ModelSourcesPath = GetString(global, "modelSourcesPath", "model_sources_path") ?? "",
            TrustedNetworks = ParseStringArray(GetNode(global, "trustedNetworks", "trusted_networks")),
            TrustedApiKeys = ParseStringArray(GetNode(global, "trustedApiKeys", "trusted_api_keys")),
            MdnsEnabled = GetBool(global, "mdnsEnabled", "mdns_enabled") ?? true,
            MdnsServiceName = GetString(global, "mdnsServiceName", "mdns_service_name") ?? "",
            WindowsServiceName = GetString(global, "windowsServiceName", "windows_service_name") ?? "",
            Debug = ParseDebug(GetObject(global, "debug") ?? []),
            Provider = ParseProvider(GetObject(global, "provider") ?? []),
            LogLevel = GetString(global, "logLevel", "log_level") ?? "Information",
            EnableFileLogs = GetBool(global, "enableFileLogs", "enable_file_logs") ?? false,
            LogPath = GetString(global, "logPath", "log_path") ?? ""
        };
    }

    /// <summary>
    ///     Parses the optional <c>global.debug</c> object. Every flag defaults to false (off) so a
    ///     config with no "debug" section — or an empty one — keeps normal, quiet behavior; these are
    ///     opt-in diagnostics you enable while investigating, not steady-state logging.
    /// </summary>
    private static DebugConfig ParseDebug(JsonObject debug)
    {
        return new DebugConfig
        {
            LogUnattributedRequests =
                GetBool(debug, "logUnattributedRequests", "log_unattributed_requests") ?? false,
            LogRequestBodies =
                GetBool(debug, "logRequestBodies", "log_request_bodies") ?? false
        };
    }

    /// <summary>
    ///     Parses the optional <c>global.provider</c> object, defaulting to Ollama on 127.0.0.1:11434
    ///     when absent so every existing config (which predates this section entirely) keeps working
    ///     unchanged.
    /// </summary>
    private static ProviderConfiguration ParseProvider(JsonObject provider)
    {
        Provider type = string.Equals(GetString(provider, "type"), "lmstudio", StringComparison.OrdinalIgnoreCase)
            ? Provider.LMStudio
            : Provider.Ollama;

        return new ProviderConfiguration
        {
            Type = type,
            Host = GetString(provider, "host") ?? "127.0.0.1",
            Port = GetInt(provider, "port") ?? (type == Provider.LMStudio ? 1234 : 11434),
            ApiKey = GetString(provider, "apiKey", "api_key"),
            TimeoutSeconds = GetInt(provider, "timeoutSeconds", "timeout_seconds")
        };
    }

    /// <summary>
    ///     Parses <c>global.draftMode</c>, defaulting to <see cref="DraftMode.Preview" /> for any
    ///     missing/unrecognized value so existing configs (and every test that never sets this)
    ///     keep the original draft-answer-preview behavior unchanged.
    /// </summary>
    private static DraftMode ParseDraftMode(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "CLEANUP" => Configuration.DraftMode.Cleanup,
            "BOTH" => Configuration.DraftMode.Both,
            _ => Configuration.DraftMode.Preview
        };
    }

    private static string[] ParseStringArray(JsonNode? node)
    {
        if (node is not JsonArray arr)
        {
            return [];
        }

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

public sealed class GlobalConfig : IGlobalConfig
{
    public string DefaultAgentType { get; set; } = "code";
    public HardwareSetting GpuLayers { get; set; } = HardwareSetting.Unspecified;
    public HardwareSetting CpuThreads { get; set; } = HardwareSetting.Unspecified;
    public string DraftModel { get; set; } = "";
    public string MainModel { get; set; } = "";

    /// <summary>
    ///     Embedding model for semantic search. Empty = fall back to the 'embed' model role in config.json, then
    ///     'nomic-embed-text'.
    /// </summary>
    public string EmbedModel { get; set; } = "";

    public int OllamaPort { get; set; } = 11434;
    public int RouterPort { get; set; } = 11435;

    /// <summary>
    ///     Hostname or IP the router HttpListener binds to. Default "localhost".
    ///     Use "*" or "+" to bind all interfaces (requires admin / URL ACL).
    /// </summary>
    public string RouterHost { get; set; } = "localhost";

    public string RouterModelName { get; set; } = "HelperModel";
    public int? ContextSize { get; set; }
    public string AgentType { get; set; } = "";

    /// <summary>
    ///     How long (in milliseconds) SimpleModelPairGenerationService lets the draft (0.5b) model
    ///     stream before handing off to the main (3b) model, whatever the draft has produced so
    ///     far. This is a *handoff cutoff*, not a wait-for-completion timeout: if the draft
    ///     finishes sooner, handoff happens immediately; if it's still going, whatever text has
    ///     streamed by this point is what gets spliced into the main model's prompt (see
    ///     <see cref="Configuration.DraftMode" />), and the rest of the draft's output is drained
    ///     in the background and discarded. Default 900ms.
    /// </summary>
    public int DraftHandoffTimeoutMs { get; set; } = 900;

    /// <summary>
    ///     What the draft (0.5b) model's output is used for before main (3b) runs. See
    ///     <see cref="Configuration.DraftMode" /> for the three options. Default
    ///     <see cref="Configuration.DraftMode.Preview" /> preserves the original draft-pair
    ///     behavior.
    /// </summary>
    public DraftMode DraftMode { get; set; } = DraftMode.Preview;

    /// <summary>
    ///     Legacy: path to separate model-sources file. Ignored when SimpleModelServer.config.json
    ///     contains an inline "models" section.
    /// </summary>
    public string ModelSourcesPath { get; init; } = "";

    /// <summary>
    ///     IPv4 networks allowed to connect to the router, expressed as dotted-quad network
    ///     addresses whose trailing zero octets imply the mask width (e.g. "192.168.0.0" for a
    ///     /16, "192.168.1.0" for a /24 — see <see cref="SimpleModelServer.Server.Infrastructure.Networking.TrustedNetwork" />).
    ///     CIDR form ("10.0.0.0/8") is also accepted. Empty (the default) means unrestricted —
    ///     every client is allowed, matching prior behavior. Loopback is always allowed
    ///     regardless of this setting.
    /// </summary>
    public IReadOnlyList<string> TrustedNetworks { get; set; } = [];

    /// <summary>
    ///     API keys that are trusted for authentication. Requests from localhost/127.0.0.1
    ///     are always allowed regardless of API key. Remote requests require a valid API key from this list.
    /// </summary>
    public IReadOnlyList<string> TrustedApiKeys { get; set; } = [];

    /// <summary>
    ///     When true (the default), the router advertises itself over mDNS
    ///     (<c>_draftmodel._tcp.local.</c>) so LAN clients can discover host/port without a
    ///     hardcoded config — see <see cref="SimpleModelServer.Library.Networking.MdnsAdvertiser" /> and
    ///     docs/mdns-discovery.md. Set to false to disable (e.g. locked-down networks where
    ///     multicast is blocked or undesirable).
    /// </summary>
    public bool MdnsEnabled { get; set; } = true;

    /// <summary>
    ///     Overrides the advertised mDNS service instance name. Empty (the default) uses
    ///     "SimpleModelServer {hostname}" with the hostname sanitized per
    ///     <see cref="SimpleModelServer.Library.Networking.MdnsAdvertiser.SafeInstanceLabel" />.
    /// </summary>
    public string MdnsServiceName { get; set; } = "";

    /// <summary>
    ///     Name of the Windows service this process runs under, purely for logging when
    ///     <see cref="SimpleModelServer.Loader.ModelHealthMonitor" /> escalates to a full process restart
    ///     after sustained health-check failures survive Ollama-only recovery (see
    ///     ModelHealthMonitor.RequestProcessRestart / RestartWindowsServiceOrExitProcess). Defaults
    ///     to "SimpleModelServerLoader" to match the name both
    ///     build/scripts/install-simplemodelserver-service.ps1 and build/wix/SimpleModelServer.Setup/Service.wxs
    ///     register it under — those installers also configure
    ///     <c>sc failure SimpleModelServerLoader reset=86400 actions=restart/5000/restart/10000/restart/30000</c>,
    ///     so the actual restart mechanism is just this process exiting non-zero; there's nothing
    ///     to invoke via this name beyond the log line unless a future deployment mechanism needs it.
    /// </summary>
    public string WindowsServiceName { get; init; } = "SimpleModelServerLoader";

    /// <summary>
    ///     Opt-in diagnostic toggles (config <c>global.debug</c>). Off by default — enable
    ///     individual flags while investigating, e.g. to fingerprint unattributed clients. See
    ///     <see cref="DebugConfig" />.
    /// </summary>
    public IDebugConfig Debug { get; init; } = new DebugConfig();

    /// <summary>
    ///     Which inference backend serves generation requests. See <see cref="IGlobalConfig.Provider" />.
    /// </summary>
    public IProviderConfiguration Provider { get; init; } = new ProviderConfiguration();

    /// <summary>
    ///     Minimum log level for all outputs. Default "Information".
    ///     Serilog levels: "Verbose", "Debug", "Information", "Warning", "Error", "Fatal".
    /// </summary>
    public string LogLevel { get; init; } = "Information";

    /// <summary>
    ///     When true, logs are also written to a file at <see cref="LogPath" />.
    ///     Default false — file logging is opt-in.
    /// </summary>
    public bool EnableFileLogs { get; init; } = false;

    /// <summary>
    ///     Path to the log file when <see cref="EnableFileLogs" /> is true.
    ///     Empty = use the default path ({ProgramData}\SimpleModelServer\logs\log-.txt).
    ///     Supports template placeholders like {Date:yyyyMMdd}.
    /// </summary>
    public string LogPath { get; init; } = "";
}

/// <summary>
///     Opt-in diagnostic toggles under config <c>global.debug</c>. Each flag is independent and
///     defaults to false so normal operation stays quiet; turn one on to get extra logging for a
///     specific investigation, then turn it back off. Add new one-off diagnostics here rather than
///     scattering ad-hoc <c>#if DEBUG</c> or always-on verbose logging through the code.
/// </summary>
public sealed class DebugConfig : IDebugConfig
{
    /// <summary>
    ///     When true, any request that arrives with no <c>User-Agent</c> header is logged with its
    ///     full fingerprint (every header — Authorization redacted to its scheme — plus remote
    ///     endpoint, IP, HTTP version, keep-alive). Used to discover whether an unidentified client
    ///     (e.g. a background embeddings indexer) carries some other usable signal. Off by default.
    /// </summary>
    public bool LogUnattributedRequests { get; init; }

    /// <summary>
    ///     When true, POST request bodies are logged (truncated) as they're routed. Verbose and
    ///     potentially sensitive — intended only for short local debugging sessions. Off by default.
    /// </summary>
    public bool LogRequestBodies { get; init; }
}

/// <summary>
///     What the draft (0.5b) model is used for in SimpleModelPairGenerationService before the main (3b)
///     model runs. All three still return the main model's output as the final answer — they only
///     differ in what the draft model contributes to main's prompt.
/// </summary>
public enum DraftMode
{
    /// <summary>
    ///     Draft streams a preview *answer* to the original prompt (up to
    ///     <see cref="GlobalConfig.DraftHandoffTimeoutMs" />), which is spliced into main's prompt
    ///     as "here's a draft answer — verify, correct, and complete it." Risk: an confidently
    ///     wrong draft answer can anchor main on the same mistake. This is the original DraftModel
    ///     behavior and the default.
    /// </summary>
    Preview,

    /// <summary>
    ///     Draft rewrites the user's *prompt* (fixes typos/grammar, expands shorthand, makes an
    ///     ambiguous ask explicit) and that rewritten prompt — not any draft answer — is what main
    ///     actually generates a response to. No draft answer, so nothing to fall back to if main
    ///     comes back empty (falls back to the original prompt's own text isn't meaningful either;
    ///     an empty main response stays empty in this mode).
    /// </summary>
    Cleanup,

    /// <summary>
    ///     Draft first rewrites the prompt (as in <see cref="Cleanup" />), then drafts a preview
    ///     answer to the *cleaned* prompt (as in <see cref="Preview" />), and main receives both.
    ///     Highest potential accuracy, at the cost of two sequential draft calls before main starts
    ///     (added latency).
    /// </summary>
    Both
}

public sealed class AgentConfig : IAgentConfig
{
    public string SystemPrompt { get; set; } = "";
    public double? Temperature { get; set; }
    public JsonNode? Sampling { get; set; }
}

public sealed class ModelSourceConfig : IModelSourceConfig
{
    public string Base { get; init; } = "";
    public int? NumCtx { get; init; }
    public string? KeepAlive { get; init; }

    /// <summary>
    ///     Explicit GPU layer count pin. Null (the default) means "let GpuBudgetPlanner decide
    ///     based on detected VRAM"; set this to bypass auto-planning for just this one model
    ///     (e.g. to force a model fully onto CPU, or force a specific layer count you've already
    ///     hand-tuned).
    /// </summary>
    public int? NumGpu { get; init; }

    public IReadOnlyList<string> Stop { get; init; } = [];

    /// <summary>
    ///     Model family (e.g., "deepseek", "qwen2.5"). Used with <see cref="Variant" /> and
    ///     <see cref="Size" /> to auto-generate the model name and base if not explicitly provided.
    /// </summary>
    public string? Family { get; init; }

    /// <summary>
    ///     Model variant (e.g., "coder", "instruct"). Used with <see cref="Family" /> and
    ///     <see cref="Size" /> to auto-generate the model name and base if not explicitly provided.
    /// </summary>
    public string? Variant { get; init; }

    /// <summary>
    ///     Model size (e.g., "0.5b", "3b", "7b"). Used with <see cref="Family" /> and
    ///     <see cref="Variant" /> to auto-generate the model name and base if not explicitly provided.
    /// </summary>
    public string? Size { get; init; }

    /// <summary>
    ///     Chat suffix to append when auto-generating model name and base (e.g., "-chat", "-instruct").
    ///     Defaults to "-chat" when not specified. Set to empty string to disable.
    ///     Only used when auto-generating from <see cref="Family" />, <see cref="Variant" />, and <see cref="Size" />.
    /// </summary>
    public string? ChatSuffix { get; init; }
}

public readonly record struct HardwareSetting(bool IsSpecified, bool Auto, int? Value)
{
    public static HardwareSetting Unspecified { get; } = new(false, false, null);
    public static HardwareSetting Automatic { get; } = new(true, true, null);

    public static HardwareSetting Override(int value)
    {
        return new HardwareSetting(true, false, value);
    }
}
