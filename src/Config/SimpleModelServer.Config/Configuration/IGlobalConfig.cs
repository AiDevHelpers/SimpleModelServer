namespace SimpleModelServer.Config.Configuration;

using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Providers;

public interface IGlobalConfig
{
    // Settings the admin config-update API can change live — see GlobalSettingRegistry in
    // SimpleModelServer.Server — need a setter here so ConfigurationUpdater can apply changes
    // through the typed interface instead of `dynamic`.
    string DefaultAgentType { get; set; }
    HardwareSetting GpuLayers { get; set; }
    HardwareSetting CpuThreads { get; set; }
    string DraftModel { get; set; }
    string MainModel { get; set; }
    string EmbedModel { get; set; }
    int OllamaPort { get; set; }
    int RouterPort { get; set; }
    string RouterHost { get; set; }
    string RouterModelName { get; set; }
    int? ContextSize { get; set; }
    string AgentType { get; set; }
    int DraftHandoffTimeoutMs { get; set; }
    DraftMode DraftMode { get; set; }
    IReadOnlyList<string> TrustedNetworks { get; set; }
    IReadOnlyList<string> TrustedApiKeys { get; set; }
    bool MdnsEnabled { get; set; }
    string MdnsServiceName { get; set; }

    // Not admin-updatable at runtime — read-only.
    string ModelSourcesPath { get; }
    string WindowsServiceName { get; }
    IDebugConfig Debug { get; }

    /// <summary>
    ///     Which inference backend (Ollama, LM Studio) serves generation requests. Config-file-only
    ///     (config.json "provider" section) — changing it requires a restart, since it determines
    ///     which process-manager/generation-backend wiring gets constructed at startup. Not exposed
    ///     via the admin hot-config API.
    /// </summary>
    IProviderConfiguration Provider { get; }
}
