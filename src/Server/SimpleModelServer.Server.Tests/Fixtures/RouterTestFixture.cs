using System;
using System.Collections.Generic;
using SimpleModelServer.Common.Infrastructure;
using SimpleModelServer.Common.Interfaces;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Hardware;
using SimpleModelServer.Hardware.Interfaces;
using SimpleModelServer.Providers.Common.Generation;
using SimpleModelServer.Providers.Common.Utilities;
using SimpleModelServer.Providers.Ollama;
using SimpleModelServer.Server.Configuration;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SimpleModelServer.Contracts.Agents;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Wires up a real <see cref="RouterHttpServer" /> bound to an ephemeral loopback port, backed
///     by a real <see cref="FakeOllamaServer" /> instead of an actual Ollama install, and exposes
///     an <see cref="HttpClient" /> preconfigured with the router's base address. This is the
///     shared harness every endpoint test in this project builds on: it drives the router exactly
///     the way a real client (curl, TokenSaver.MCP, the VS Code extension) would, over real
///     sockets, rather than calling internal methods directly.
/// </summary>
internal sealed class RouterTestFixture : IAsyncDisposable
{
    public FakeOllamaServer Ollama { get; }
    public IAppConfig Config { get; }
    public HttpClient Client { get; }
    public string BaseAddress { get; }

    /// <summary>
    ///     Exposes the live router instance so tests can reach test-only hooks like
    ///     <see cref="RouterHttpServer.ExtraSelfTestChecks" /> — everything else should go through
    ///     <see cref="Client" /> to stay a true end-to-end HTTP test.
    /// </summary>
    public RouterHttpServer Router => router;

    /// <summary>
    ///     Exposes the live <see cref="ModelHealthMonitor" /> so tests can drive
    ///     <see cref="ModelHealthMonitor.EvaluatePeriodicHealthAsync" /> and override
    ///     <see cref="ModelHealthMonitor.RequestProcessRestart" /> directly, the same way
    ///     <see cref="Router" /> exposes <see cref="RouterHttpServer.ExtraSelfTestChecks" />.
    /// </summary>
    public ModelHealthMonitor Health => health;

    private readonly RouterHttpServer router;
    private readonly ModelHealthMonitor health;
    private readonly OllamaProcessManager process;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task serverTask;
    private readonly HttpClient routerInternalHttp;

    public string CurrentAgentType { get; set; }

    private RouterTestFixture(
        FakeOllamaServer ollama,
        AppConfig config,
        int routerPort,
        HttpClient routerInternalHttp,
        OllamaProcessManager process,
        ModelHealthMonitor health,
        RouterHttpServer router)
    {
        Ollama = ollama;
        Config = config;
        this.routerInternalHttp = routerInternalHttp;
        this.process = process;
        this.health = health;
        this.router = router;
        BaseAddress = $"http://127.0.0.1:{routerPort}";
        Client = new HttpClient { BaseAddress = new Uri(BaseAddress) };
        Client.DefaultRequestHeaders.Add("User-Agent", "SimpleModelServer.Server.Tests");
        CurrentAgentType = config.DefaultAgentType;

        serverTask = this.router.StartAsync(routerPort, shutdown.Token);
    }

    /// <summary>
    ///     Builds the fixture with sane test defaults (unrestricted trusted networks, "code" as
    ///     the default/only agent). Callers can pass a <paramref name="configureConfig" /> hook to
    ///     override config (e.g. to set trustedNetworks) before the router starts.
    /// </summary>
    public static async Task<RouterTestFixture> CreateAsync(Func<AppConfig, AppConfig>? configureConfig = null)
    {
        FakeOllamaServer ollama = new();
        ollama.Start();

        AppConfig config = new()
        {
            Global = new GlobalConfig
            {
                DefaultAgentType = "code",
                OllamaPort = ollama.Port,
                RouterHost = "127.0.0.1",
                RouterModelName = "TestRouterModel"
            },
            Agents = new Dictionary<string, IAgentConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["code"] = new AgentConfig() { SystemPrompt = "You write code.", Temperature = 0.1 }
            }
        };

        if (configureConfig is not null)
        {
            config = configureConfig(config);
        }

        HttpClient sharedHttp = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
        OllamaProcessManager process = new(config, NullLogger.Instance, sharedHttp);

        ResolvedHardwareSettings hardware = new()
        {
            GpuLayers = null,
            CpuThreads = null,
            Gpu = GpuInfo.None,
            MaxConcurrentSummaryRequests = 1
        };

        string currentAgentType = config.DefaultAgentType;

        IOllamaHelpers ollamaHelpers = new OllamaHelpers();
        IConsoleHelper consoleHelper = new ConsoleHelper();
        IGenerationBackend generationBackend = new OllamaGenerationBackend(process, sharedHttp, ollamaHelpers);

        SimpleModelPairGenerationService generation = new(
            config, NullLogger.Instance, generationBackend,
            () => hardware,
            _ => null,
            _ => Task.CompletedTask,
            ollamaHelpers,
            consoleHelper);

        AgentTypeResolver agentTypeResolver = new(config);
        ModelHealthMonitor health = new(config, NullLogger.Instance, process, sharedHttp, _ => null);

        IHardwareDetection hardwareDetection = new HardwareDetection();
        McpUpstreamRegistry mcpUpstreamRegistry = new(config.McpServers, NullLogger.Instance);
        SystemInfoService systemInfoService = new(config, hardwareDetection, mcpUpstreamRegistry);

        // Stub implementations for test
        IConfigurationManager configManager = new StubConfigurationManager();
        IConfigurationUpdater configUpdater = new StubConfigurationUpdater();
        IAdminAuthenticationService adminAuth = new StubAdminAuthenticationService();
        ModelReloadCoordinator reloadCoordinator = new(process, health, NullLogger.Instance, TimeSpan.FromSeconds(5));
        AdminEndpointHandler admin = new(config, NullLogger.Instance, configManager, configUpdater, adminAuth, reloadCoordinator);

        RouterHttpServer router = new(
            config, NullLogger.Instance, process, health, generation, agentTypeResolver, sharedHttp,
            () => hardware,
            () => currentAgentType,
            ollamaHelpers,
            systemInfoService,
            admin,
            reloadCoordinator,
            mcpUpstreamRegistry);

        int routerPort = GetFreeTcpPort();

        RouterTestFixture fixture = new(ollama, config, routerPort, sharedHttp, process, health, router);

        // Give the HttpListener a moment to actually bind before tests start firing requests.
        await WaitForRouterReadyAsync(fixture).ConfigureAwait(false);
        return fixture;
    }

    private static async Task WaitForRouterReadyAsync(RouterTestFixture fixture)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        while (!cts.IsCancellationRequested)
        {
            try
            {
                using HttpResponseMessage response =
                    await fixture.Client.GetAsync(new Uri("/api/version", UriKind.Relative), cts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Router not bound yet — retry.
            }

            await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
        }

        throw new TimeoutException("Router did not become ready in time.");
    }

    private static int GetFreeTcpPort()
    {
        TcpListener tcpListener = new(IPAddress.Loopback, 0);
        tcpListener.Start();
        int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await shutdown.CancelAsync();
        try
        {
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // best effort — listener.Stop() inside StartAsync's ct.Register should unwind this.
        }

        Client.Dispose();
        routerInternalHttp.Dispose();
        process.Dispose();
        Ollama.Dispose();
        shutdown.Dispose();
    }
}

/// <summary>Stub implementation for tests.</summary>
internal sealed class StubConfigurationManager : IConfigurationManager
{
    public bool RequiresRestart(string settingPath) => false;
    public bool CanHotReload(string settingPath) => true;
    public ValidationResult Validate(string settingPath, object? value)
        => ValidationResult.Valid();
    public ValidationResult ValidateAgentField(string fieldName, object? value)
        => ValidationResult.Valid();
}

/// <summary>Stub implementation for tests.</summary>
internal sealed class StubConfigurationUpdater : IConfigurationUpdater
{
    public UpdateResult UpdateGlobalSetting(IGlobalConfig config, string settingName, object? value)
        => new(true, null, false);
    public UpdateResult UpdateAgentSetting(IReadOnlyDictionary<string, IAgentConfig> agents, string agentName, string fieldName, object? value)
        => new(true, null, false);
}

/// <summary>Stub implementation for tests.</summary>
internal sealed class StubAdminAuthenticationService : IAdminAuthenticationService
{
    public bool IsAuthorized(HttpRequest request) => true;
}


