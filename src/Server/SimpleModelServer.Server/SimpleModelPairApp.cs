using SimpleModelServer.Common.Infrastructure;
using SimpleModelServer.Common.Interfaces;
using SimpleModelServer.Contracts.Dtos.Providers;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Hardware;
using SimpleModelServer.Hardware.Interfaces;
using SimpleModelServer.Providers.Common.Generation;
using SimpleModelServer.Providers.LMStudio;
using SimpleModelServer.Providers.Ollama;
using SimpleModelServer.Server.Configuration;
using SimpleModelServer.Server.Infrastructure.Networking;

using Microsoft.Extensions.Logging;

using System.Globalization;

namespace SimpleModelServer.Server;

/// <summary>
///     Composition root for the Ollama draft-pair router. All status/diagnostic output goes
///     through the injected <see cref="ILogger" /> rather than directly to Console, so this
///     behaves identically whether launched as an interactive console process (F5 debug,
///     --service/--router from a terminal) or as a Windows Service under the SCM (which has no
///     console at all — see SimpleModelServerWorker). The one exception is
///     <see cref="ConsoleHelper.WriteOverwrite" /> (used inside
///     <see cref="SimpleModelPairGenerationService" />), which drives a live-updating token stream for
///     interactive terminal use only and is not a log event.
///     Logging goes through a plain Microsoft.Extensions.Logging console logger configured in
///     Program.cs — no EventLog/VS Code sink. Console output is invisible under the SCM (no
///     console session for a Windows service); see Program.cs's file-based Trace() diagnostic
///     for that case.
///
///     This class only orchestrates startup/shutdown and wires together the single-purpose
///     collaborators that do the actual work:
///     <list type="bullet">
///         <item><see cref="OllamaProcessManager" /> — starting/killing/probing the Ollama process and models.</item>
///         <item><see cref="ModelHealthMonitor" /> — background health checks, warmup, and recovery.</item>
///         <item><see cref="AgentTypeResolver" /> — determining which agent type a request targets.</item>
///         <item><see cref="SimpleModelPairGenerationService" /> — dual-stream generation and research routing.</item>
///         <item><see cref="RouterHttpServer" /> — the HTTP listener loop and request handlers.</item>
///     </list>
/// </summary>
internal sealed partial class SimpleModelPairApp : IDisposable
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Default agent: {DefaultAgentType}")]
    private static partial void LogDefaultAgent(ILogger logger, string defaultAgentType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trusted networks: {TrustedNetworks} (+ loopback always allowed)")]
    private static partial void LogTrustedNetworks(ILogger logger, string trustedNetworks);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ollama already running at http://localhost:{ServerPort} (PID: {ProcessId})")]
    private static partial void LogOllamaAlreadyRunning(ILogger logger, int serverPort, int? processId);

    [LoggerMessage(Level = LogLevel.Information, Message = "\nRouter running at http://{RouterHost}:{RouterPort}")]
    private static partial void LogRouterRunning(ILogger logger, string routerHost, int routerPort);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ollama running at http://localhost:{ServerPort}")]
    private static partial void LogOllamaRunning(ILogger logger, int serverPort);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hardware: GPU={Gpu}, gpuLayers={GpuLayers}, cpuThreads={CpuThreads}")]
    private static partial void LogHardwareSettings(ILogger logger, string gpu, string gpuLayers, string cpuThreads);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent settings active: {AgentType}")]
    private static partial void LogAgentSettingsActive(ILogger logger, string agentType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent: {PreviousAgentType} → {AgentType} (models unchanged, no reload needed)")]
    private static partial void LogAgentSwitch(ILogger logger, string previousAgentType, string agentType);

    [LoggerMessage(Level = LogLevel.Information, Message = "=== OLLAMA DRAFT PAIR ROUTER ===")]
    private static partial void LogRouterStart(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trusted networks: unrestricted (global.trustedNetworks is empty)")]
    private static partial void LogTrustedNetworksUnrestricted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "'ollama' not found.")]
    private static partial void LogOllamaNotFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ollama not responsive. Killing stuck processes and restarting...")]
    private static partial void LogOllamaNotResponsive(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "mDNS advertising disabled (global.mdnsEnabled = false).")]
    private static partial void LogMdnsDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "mDNS: advertising router as \"{ServiceName}\" ({ServiceType}) on port {Port}.")]
    private static partial void LogMdnsAdvertising(ILogger logger, string serviceName, string serviceType, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "mDNS advertising failed to start — router is still reachable directly, just not LAN-discoverable.")]
    private static partial void LogMdnsAdvertisingFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Summary}")]
    private static partial void LogGpuPlanSummary(ILogger logger, string summary);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GPU layer planning failed — falling back to global gpuLayers setting.")]
    private static partial void LogGpuPlanningFailed(ILogger logger, Exception ex);

    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan RestartSettleDelay = TimeSpan.FromMilliseconds(500);

    private readonly AppOptions options;
    private readonly IAppConfig config;
    private readonly ILogger logger;
    // UseProxy: false — see the matching comment on OllamaProcessManager.probeHttp; this client
    // also only ever talks to the local Ollama process on loopback.
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false }) { Timeout = HttpClientTimeout };

    private readonly IOllamaProcessManager process;
    private readonly ModelHealthMonitor health;
    private readonly SimpleModelPairGenerationService generation;
    private readonly AgentTypeResolver agentTypeResolver;
    private readonly RouterHttpServer router;
    private readonly MdnsAdvertiser mdns;
    private readonly IHardwareDetection hardwareDetection;
    private readonly IGpuBudgetPlanner gpuBudgetPlanner;
    private readonly IOllamaHelpers ollamaHelpers;
    private readonly IConsoleHelper consoleHelper;

    private string? loadedAgentType;
    private CancellationTokenSource? routerShutdown;
    private bool disposed;
    private GpuAllocationPlan gpuPlan = GpuAllocationPlan.Empty;

    // TEMPORARY DIAGNOSTIC parameter (nullable, defaults to no-op) — see the matching comment
    // in Program.cs/SimpleModelServerWorker.cs. Writes straight to a file, bypassing ILogger, since
    // Console/ILogger output is invisible under the SCM. Remove once the current
    // service-startup investigation concludes.
    private readonly Action<string> trace;

    public SimpleModelPairApp(AppOptions options, IAppConfig config, ILogger logger, IHardwareDetection hardwareDetection, IGpuBudgetPlanner gpuBudgetPlanner, IOllamaHelpers ollamaHelpers, IConsoleHelper consoleHelper, Action<string>? trace = null)
    {
        this.options = options;
        this.config = config;
        this.logger = logger;
        this.hardwareDetection = hardwareDetection;
        this.gpuBudgetPlanner = gpuBudgetPlanner;
        this.ollamaHelpers = ollamaHelpers;
        this.consoleHelper = consoleHelper;
        this.trace = trace ?? (_ => { });

        IGenerationBackend generationBackend;
        if (config.Global.Provider.Type == Provider.LMStudio)
        {
            // No process-lifecycle manager for LM Studio — health-check only, no process control.
            // The user is expected to already have it running with its local server enabled.
            var lmStudioProvider = new LMStudioProvider((ProviderConfiguration)config.Global.Provider, http);
            process = new LMStudioProcessManagerAdapter(lmStudioProvider, config.Global.Provider);
            generationBackend = new LMStudioGenerationBackend(lmStudioProvider);
        }
        else
        {
            var ollamaProcess = new OllamaProcessManager(config, logger, http);
            process = ollamaProcess;
            generationBackend = new OllamaGenerationBackend(ollamaProcess, http, ollamaHelpers);
        }

        health = new ModelHealthMonitor(config, logger, process, http, GpuLayersFor) { OnRecovered = OnHealthRecoveredAsync };
        generation = new SimpleModelPairGenerationService(config, logger, generationBackend, () => HardwareSettings, GpuLayersFor, ReloadModelForAgentAsync, ollamaHelpers, consoleHelper);
        agentTypeResolver = new AgentTypeResolver(config);
        var mcpUpstreamRegistry = new McpUpstreamRegistry(config.McpServers, logger);
        var systemInfoService = new SystemInfoService(config, hardwareDetection, mcpUpstreamRegistry);

        // Configuration management services for admin API
        IConfigurationManager configurationManager = new ConfigurationManager();
        IConfigurationUpdater configurationUpdater = new ConfigurationUpdater(configurationManager);
        IAdminAuthenticationService adminAuthenticationService = new AdminAuthenticationService(config);
        var reloadCoordinator = new ModelReloadCoordinator(process, health, logger, TimeSpan.FromSeconds(5));
        var admin = new AdminEndpointHandler(config, logger, configurationManager, configurationUpdater, adminAuthenticationService, reloadCoordinator, mcpUpstreamRegistry);

        router = new RouterHttpServer(config, logger, process, health, generation, agentTypeResolver, http, () => HardwareSettings, () => CurrentAgentType, ollamaHelpers, systemInfoService, admin, reloadCoordinator, mcpUpstreamRegistry);
        mdns = new MdnsAdvertiser(
            Environment.MachineName,
            RouterPort,
            string.IsNullOrWhiteSpace(config.Global.MdnsServiceName) ? null : config.Global.MdnsServiceName);
    }

    private int RouterPort => options.RouterPortOverride ?? config.Global.RouterPort;

    private ResolvedHardwareSettings HardwareSettings
        => field ??= hardwareDetection.Resolve(config.Global);

    private string CurrentAgentType => loadedAgentType ?? config.DefaultAgentType;

    /// <summary>
    ///     Parsed from global.trustedNetworks. Empty configuration means unrestricted access
    ///     (matches prior behavior); loopback is always allowed regardless of this setting.
    ///     Logged once at startup for operator visibility.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            // No console attached (Windows service, or output redirected). Nothing to clear, and
            // this is expected, not an error.
        }

        LogRouterStart(logger);
        LogDefaultAgent(logger, config.DefaultAgentType);
        TrustedNetworkList trustedNetworks = TrustedNetworkList.FromConfig(config.Global.TrustedNetworks);
        if (trustedNetworks.IsRestricted)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                // CA1873 false positive: string.Join only runs inside this IsEnabled guard, so
                // it's never evaluated when Information logging is disabled.
#pragma warning disable CA1873
                LogTrustedNetworks(logger, string.Join(", ", config.Global.TrustedNetworks));
#pragma warning restore CA1873
            }
        }
        else
        {
            LogTrustedNetworksUnrestricted(logger);
        }

        trace("RunAsync: about to WriteHardwareSettings (GPU/WMI detection)...");
        WriteHardwareSettings();
        trace("RunAsync: WriteHardwareSettings done.");

        if (options.Kill)
        {
            process.KillStuckOllama();
            return 0;
        }

        if (config.Global.Provider.Type == Provider.Ollama)
        {
            // "ollama" on PATH is only required when Ollama is actually the selected backend —
            // LM Studio doesn't have (or need) a CLI binary; startup for it just probes reachability.
            trace("RunAsync: checking CommandExists(\"ollama\") via PATH lookup...");
            if (!OllamaProcessManager.CommandExists("ollama"))
            {
                trace("RunAsync: 'ollama' NOT found on PATH. Returning 1.");
                LogOllamaNotFound(logger);
                return 1;
            }
        }

        trace("RunAsync: 'ollama' found on PATH. Checking IsOllamaReadyAsync()...");
        if (await process.IsOllamaReadyAsync())
        {
            trace("RunAsync: Ollama already responsive — registering existing process.");
            process.RegisterExistingOllamaProcess();
            LogOllamaAlreadyRunning(logger, process.ServerPort, process.OllamaProcessId);
        }
        else
        {
            trace("RunAsync: Ollama not responsive. Killing stuck processes...");
            LogOllamaNotResponsive(logger);
            process.KillStuckOllama();
            await Task.Delay(RestartSettleDelay, ct);

            trace("RunAsync: calling StartServerAsync() (Process.Start(\"ollama\", \"serve\"))...");
            if (!await process.StartServerAsync())
            {
                trace("RunAsync: StartServerAsync() returned false. Returning 1.");
                return 1;
            }

            trace("RunAsync: StartServerAsync() succeeded.");
        }

        trace("RunAsync: ensuring models (draft)...");
        await process.EnsureModelAsync(generation.DraftModelName);
        trace("RunAsync: ensuring models (main)...");
        await process.EnsureModelAsync(generation.MainModelName);
        trace("RunAsync: ensuring models (embed)...");
        await process.EnsureModelAsync(generation.EmbedModelName);
        trace("RunAsync: all models ensured.");

        trace("RunAsync: planning per-model GPU layer budget...");
        await PlanGpuLayersAsync(ct);
        trace("RunAsync: GPU layer budget planned.");

        loadedAgentType = null;
        trace("RunAsync: calling ReloadModelForAgentAsync...");
        await ReloadModelForAgentAsync(config.DefaultAgentType);
        trace("RunAsync: ReloadModelForAgentAsync done.");

        // Load all three models into VRAM before the router starts accepting traffic. Without
        // this, EnsureModelAsync above only guarantees the models are pulled/created on disk —
        // nothing actually calls /api/generate to bring them into VRAM, so the periodic
        // ModelHealthMonitor.RunAsync loop (started below) was the *first* thing to warm them,
        // and that loop's own Task.Delay(HealthCheckInterval) means a ~15s window at every
        // startup/restart where /health legitimately reports "0 models loaded in VRAM" (the
        // "Degraded ... none are currently loaded in VRAM" message this was reported against).
        // Warming here, synchronously, before LogRouterRunning/router.StartAsync, means the
        // router doesn't come up and start reporting ready until draft, main, AND embed are all
        // actually resident.
        trace("RunAsync: warming draft/main/embed models into VRAM before accepting traffic...");
        await health.WarmupModelsAsync([generation.DraftModelName, generation.MainModelName, generation.EmbedModelName]);
        trace("RunAsync: startup VRAM warmup complete.");

        LogRouterRunning(logger, config.Global.RouterHost, RouterPort);
        LogOllamaRunning(logger, process.ServerPort);

        routerShutdown = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : new CancellationTokenSource();
        // Embed is included here (not just draft/main) so it doesn't silently idle back out of
        // VRAM between requests and reopen the same "not loaded" window this fix closes at
        // startup — see the warmup call above for the fuller rationale.
        _ = health.RunAsync(() => [generation.DraftModelName, generation.MainModelName, generation.EmbedModelName], routerShutdown.Token);

        await router.StartAsync(RouterPort, routerShutdown.Token, onBound: StartMdnsAdvertising);
        return 0;
    }

    /// <summary>
    ///     Starts mDNS advertising once the router has actually bound its port (see the
    ///     <c>onBound</c> callback passed to <see cref="RouterHttpServer.StartAsync" />) — a
    ///     process that detected an already-running router and skipped its own bind must not
    ///     also advertise a service it isn't the one serving. Controlled by
    ///     <see cref="GlobalConfig.MdnsEnabled" /> (default true); failures are logged and
    ///     swallowed rather than treated as fatal — mDNS is a discovery convenience, not a
    ///     dependency of the router actually working (matches the "diagnostic must never break
    ///     startup" precedent already established for the service-startup trace logging above).
    /// </summary>
    private void StartMdnsAdvertising()
    {
        if (!config.Global.MdnsEnabled)
        {
            LogMdnsDisabled(logger);
            return;
        }

        try
        {
            mdns.Advertise();
            LogMdnsAdvertising(logger, Environment.MachineName, MdnsAdvertiser.ServiceType, RouterPort);
        }
        catch (Exception ex)
        {
            LogMdnsAdvertisingFailed(logger, ex);
        }
    }

    private async Task OnHealthRecoveredAsync()
    {
        // Free VRAM may have shifted since last plan (Ollama itself was just killed/restarted),
        // so re-plan rather than reuse a possibly-stale allocation.
        await PlanGpuLayersAsync(CancellationToken.None);
        loadedAgentType = null;
        await ReloadModelForAgentAsync(CurrentAgentType);
    }

    private Task ReloadModelForAgentAsync(string agentType)
    {
        if (string.Equals(loadedAgentType, agentType, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        // Model names are global (not per-agent), so there is nothing to unload or reload.
        // System prompt, temperature, and sampling are all sent inline per-request via the
        // options/system fields — a model reload would only be needed if the model name changed.
        string? previous = loadedAgentType;
        loadedAgentType = agentType;

        if (string.IsNullOrWhiteSpace(previous))
        {
            LogAgentSettingsActive(logger, agentType);
        }
        else
        {
            LogAgentSwitch(logger, previous, agentType);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Runs <see cref="GpuBudgetPlanner" /> across all three models (draft, main, embed) that
    ///     share this one Ollama instance, so their combined VRAM footprint — weights and, since
    ///     it can rival weight size at larger context windows, KV cache — fits inside detected free
    ///     VRAM instead of each warmup cycle silently evicting whichever model doesn't currently
    ///     fit (the failure mode that motivated this: see ModelHealthMonitor's warmup log churn).
    ///     Priority order below is deliberate: draft (0) must stay resident since it's on the
    ///     latency-critical path of every request; embed (1) is cheap and needed for indexing, so
    ///     it's protected next; main (2) is the largest model and the least frequently invoked
    ///     (verification only), so it absorbs whatever VRAM is left over.
    /// </summary>
    private async Task PlanGpuLayersAsync(CancellationToken ct)
    {
        int defaultDraftCtx = 2048;
        int defaultMainCtx = 4096;
        int defaultEmbedCtx = 8192;

        List<GpuBudgetPlanner.ModelGpuRequest> requests =
        [
            new GpuBudgetPlanner.ModelGpuRequest(generation.DraftModelName, ResolveNumCtx(generation.DraftModelName, defaultDraftCtx), ResolveNumGpuPin(generation.DraftModelName), Priority: 0),
            new GpuBudgetPlanner.ModelGpuRequest(generation.EmbedModelName, ResolveNumCtx(generation.EmbedModelName, defaultEmbedCtx), ResolveNumGpuPin(generation.EmbedModelName), Priority: 1),
            new GpuBudgetPlanner.ModelGpuRequest(generation.MainModelName, ResolveNumCtx(generation.MainModelName, defaultMainCtx), ResolveNumGpuPin(generation.MainModelName), Priority: 2)
        ];

        try
        {
            gpuPlan = await gpuBudgetPlanner.PlanAsync(http, process.ServerPort, HardwareSettings.Gpu, requests, logger, ct);
            if (logger.IsEnabled(LogLevel.Information))
            {
                LogGpuPlanSummary(logger, gpuPlan.Summary);
            }
        }
        catch (Exception ex)
        {
            // Planning is an optimization, not a dependency — if it throws for any reason (a
            // future Ollama API shape we didn't anticipate, etc.) the router must still start,
            // just without per-model VRAM awareness (falls back to the global GpuLayers setting).
            LogGpuPlanningFailed(logger, ex);
            gpuPlan = GpuAllocationPlan.Empty;
        }
    }

    private int ResolveNumCtx(string model, int fallback)
    {
        return config.Global.ContextSize
            ?? (config.ModelSources.TryGetValue(model, out IModelSourceConfig? src) ? src.NumCtx : null)
            ?? fallback;
    }

    private int? ResolveNumGpuPin(string model)
    {
        return config.ModelSources.TryGetValue(model, out IModelSourceConfig? src) ? src.NumGpu : null;
    }

    /// <summary>Per-model GPU layer count from the last <see cref="PlanGpuLayersAsync" /> run, or null if unplanned (falls back to the global <see cref="ResolvedHardwareSettings.GpuLayers" />).</summary>
    private int? GpuLayersFor(string model)
    {
        return gpuPlan.TryGetLayers(model);
    }

    private void WriteHardwareSettings()
    {
        ResolvedHardwareSettings hw = HardwareSettings;
        if (hw.GpuLayers is null && hw.CpuThreads is null)
        {
            return;
        }

        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        // CA1873 false positive: the interpolation/ToString calls below only run after the
        // IsEnabled(Information) check above returns, so they're never evaluated when
        // Information logging is disabled.
#pragma warning disable CA1873
        string gpu = hw.Gpu.Detected ? $"{hw.Gpu.Name} ({hw.Gpu.MemoryMb} MB)" : "none";
        LogHardwareSettings(
            logger,
            gpu,
            hw.GpuLayers?.ToString(CultureInfo.InvariantCulture) ?? "unset",
            hw.CpuThreads?.ToString(CultureInfo.InvariantCulture) ?? "unset");
#pragma warning restore CA1873
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        mdns.Dispose();
        http.Dispose();
        process.Dispose();
        routerShutdown?.Dispose();
        disposed = true;
    }
}


