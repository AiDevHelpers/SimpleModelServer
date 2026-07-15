using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Dtos.Providers;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Hardware;
using SimpleModelServer.Hardware.Interfaces;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers <see cref="SystemInfoService" />'s exposure of which inference backend is active —
///     read by the VS/VSCode extensions' status bars to display "Ollama" vs "LM Studio" without
///     the extension needing any provider-specific configuration of its own (see
///     docs/Config/model-configuration.md's "Inference Provider" section: provider selection is
///     entirely server-side) — and of configured MCP upstream servers, read by the admin UI.
/// </summary>
public sealed class SystemInfoServiceTests
{
    private static SystemInfoService CreateService(
        Provider providerType = Provider.Ollama,
        Dictionary<string, IMcpUpstreamConfig>? mcpServers = null)
    {
        AppConfig config = new()
        {
            Global = new GlobalConfig
            {
                Provider = new ProviderConfiguration { Type = providerType }
            },
            McpServers = mcpServers ?? new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase)
        };

        IHardwareDetection hardwareDetection = new HardwareDetection();
        McpUpstreamRegistry mcpUpstreamRegistry = new(config.McpServers, NullLogger.Instance);
        return new SystemInfoService(config, hardwareDetection, mcpUpstreamRegistry);
    }

    [Fact]
    public void GetSystemInfo_ProviderOllama_ReportsLowercaseOllama()
    {
        SystemInfoService service = CreateService(Provider.Ollama);

        var info = service.GetSystemInfo();

        Assert.Equal("ollama", info.Configuration.Global.Provider);
    }

    [Fact]
    public void GetSystemInfo_ProviderLMStudio_ReportsLowercaseLmstudio()
    {
        SystemInfoService service = CreateService(Provider.LMStudio);

        var info = service.GetSystemInfo();

        Assert.Equal("lmstudio", info.Configuration.Global.Provider);
    }

    [Fact]
    public void GetSystemInfo_NoMcpServersConfigured_ReportsEmptyDictionary()
    {
        SystemInfoService service = CreateService();

        var info = service.GetSystemInfo();

        Assert.Empty(info.Configuration.McpServers);
    }

    [Fact]
    public void GetSystemInfo_McpServerConfiguredButNeverInitialized_ReportsNotConnected()
    {
        // InitializeAsync() is what actually connects to upstreams — this test never calls it,
        // mirroring how GetSystemInfo can be queried before/without a successful connect attempt.
        var mcpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["tokensaver"] = new McpUpstreamConfig { Path = "http://127.0.0.1:9999/mcp" }
        };
        SystemInfoService service = CreateService(mcpServers: mcpServers);

        var info = service.GetSystemInfo();

        Assert.True(info.Configuration.McpServers.ContainsKey("tokensaver"));
        var status = info.Configuration.McpServers["tokensaver"];
        Assert.Equal("http://127.0.0.1:9999/mcp", status.Path);
        Assert.False(status.Connected);
        Assert.Equal(0, status.ToolCount);
    }
}
