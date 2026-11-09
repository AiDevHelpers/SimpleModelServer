using SimpleModelServer.Config;
using SimpleModelServer.Contracts.Dtos;
using SimpleModelServer.Contracts.Dtos.Settings;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Config.Configuration;

using Microsoft.Extensions.Logging;

using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server;

using SimpleModelServer.Providers.Ollama;

/// <summary>
///     Tracks and maintains the health of Ollama and the draft/main models: periodic
///     background warmup (keeping models resident in VRAM), unhealthy-Ollama recovery, and
///     unresponsive-model reload. Depends on <see cref="OllamaProcessManager" /> for the
///     actual process control and health probes, and calls back into
///     <see cref="Func{Task}" /> hooks provided by the owner for agent-reload after recovery.
/// </summary>
internal sealed partial class ModelHealthMonitor(
    IAppConfig config,
    ILogger logger,
    IOllamaProcessManager process,
    HttpClient http,
    Func<string, int?> gpuLayersForModel,
    InFlightGenerations? inFlight = null)
{

    [LoggerMessage(Level = LogLevel.Information, Message = "Model '{Model}' reloaded successfully.")]
    private static partial void LogModelReloadedSuccessfully(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Health failed for {Count} consecutive cycles despite in-cycle recovery — exiting process (exit code 1) so Windows service '{ServiceName}' restarts via its configured sc.exe failure action.")]
    private static partial void LogHealthFailureExiting(ILogger logger, int count, string serviceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Model '{Model}' warmup started.")]
    private static partial void LogModelWarmupStarted(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Health check error: {Message}")]
    private static partial void LogHealthCheckError(ILogger logger, Exception ex, string message);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Ollama recovery failed and health threshold exceeded — {Message}")]
    private static partial void LogRecoveryFailed(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model '{Model}' failed to warm up after {Attempts} attempts.")]
    private static partial void LogWarmupFailed(ILogger logger, string model, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model '{Model}' warmup attempt failed: {Reason}")]
    private static partial void LogWarmupAttemptFailed(ILogger logger, string model, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Warmup attempt network error for '{Model}'")]
    private static partial void LogWarmupNetworkError(ILogger logger, Exception ex, string model);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Warmup attempt response for '{Model}': {StatusCode}")]
    private static partial void LogWarmupResponse(ILogger logger, string model, int statusCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Warmup attempt timeout for '{Model}' (Ollama may be busy)")]
    private static partial void LogWarmupTimeout(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ollama recovery starting: {Reason}")]
    private static partial void LogOllamaRecoveryStarting(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ollama recovery complete.")]
    private static partial void LogOllamaRecoveryComplete(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model '{Model}' unresponsive. Reloading...")]
    private static partial void LogModelUnresponsive(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model '{Model}' reload returned {StatusCode}.")]
    private static partial void LogModelReloadReturned(ILogger logger, string model, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model '{Model}' reload failed: {Message}")]
    private static partial void LogModelReloadFailed(ILogger logger, Exception ex, string model, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Models updated: Draft={Draft}({DraftLoc}), Main={Main}({MainLoc}), Embed={Embed}({EmbedLoc}), Memory={Memory:F1}GB")]
    private static partial void LogModelsUpdated(ILogger logger, string? draft, string? draftLoc, string? main, string? mainLoc, string? embed, string? embedLoc, double memory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model '{Model}' warmup error (attempt {Attempt}): {ExceptionType}. This is often a transient Ollama connection issue. Retrying on next cycle.")]
    private static partial void LogWarmupError(ILogger logger, Exception ex, string model, int attempt, string exceptionType);

    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RecoverySettleDelay = TimeSpan.FromMilliseconds(800);
    // 60s, not 30s: on a cold Ollama start the first inference includes one-time GPU kernel
    // compilation that can run ~50s on modest hardware (measured on a GTX 1660), during which the
    // model's weights are already resident but the POST hasn't returned. 30s cut that off and
    // misreported a healthy-but-cold model as a warmup failure. The AttemptWarmupAsync timeout
    // handler additionally falls back to an /api/ps residency check, so this bound only needs to
    // comfortably cover the weight-load itself, not the full first-inference compute.
    private static readonly TimeSpan ModelWarmupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ModelReloadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long an unhandled request-handler exception keeps <see cref="HasRecentFault" />
    ///     true. A stack trace on the server isn't something the periodic Ollama/model probes can
    ///     detect or fix on their own (the process itself is still up and probes may all pass),
    ///     so rather than trying to "recover" from it like an unhealthy model, it's surfaced as a
    ///     time-boxed signal: /health reports degraded while it's fresh, then it ages out so a
    ///     single transient failure doesn't wedge health reporting forever.
    /// </summary>
    private static readonly TimeSpan FaultTtl = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     How many consecutive <see cref="RunAsync" /> health-check cycles must find Ollama
    ///     and/or a warmup model still unhealthy — despite <see cref="EnsureOllamaHealthyAsync" />'s
    ///     own kill/restart recovery running every cycle — before escalating to
    ///     <see cref="RequestProcessRestart" />. At the default 15s <see cref="HealthCheckInterval" />
    ///     this is a 60s window: long enough that a single slow probe or an in-flight Ollama
    ///     recovery doesn't trigger a restart, short enough that a wedged process (Ollama recovery
    ///     itself failing repeatedly, or a model stuck refusing to (re)load) doesn't sit degraded
    ///     indefinitely — which is exactly the "still Degraded" complaint this was added for.
    /// </summary>
    private const int MaxConsecutiveUnhealthyCyclesBeforeRestart = 4;

    private readonly Lock healLock = new();
    private readonly Dictionary<string, DateTime> modelLastActive = [];
    private readonly HashSet<string> warmingUpModels = [];
    private readonly Dictionary<string, int> modelWarmupFailures = [];
    private bool recoveryInProgress;
    private string? lastFaultMessage;
    private DateTime lastFaultAtUtc;
    private int consecutiveUnhealthyCycles;
    private LoadedModelsInfo? loadedModelsInfo;

    /// <summary>Invoked after a successful unhealthy-Ollama recovery so the owner can force an agent/model reload.</summary>
    public Func<Task>? OnRecovered { get; set; }

    /// <summary>
    ///     Overridable restart action, invoked once <see cref="MaxConsecutiveUnhealthyCyclesBeforeRestart" />
    ///     consecutive periodic health cycles have found the process still unhealthy after its own
    ///     Ollama-recovery attempt. Settable by tests (to observe escalation without killing the
    ///     test process) or by non-Windows-service callers who want their own restart mechanism.
    ///     Null (the default) means "use <see cref="RestartWindowsServiceOrExitProcess" />" — see
    ///     the null-coalescing call site in <see cref="EvaluatePeriodicHealthAsync" />.
    /// </summary>
    public Action? RequestProcessRestart { get; set; }

    /// <summary>
    ///     Exits the process abnormally (non-zero) so the Windows Service Control Manager's own
    ///     recovery action restarts it — deliberately does NOT call <c>sc stop</c> / <c>sc start</c>
    ///     itself. install-draftmodel-service.ps1 and build/wix/DraftModel.Setup/Service.wxs both
    ///     register this service (name: <see cref="GlobalConfig.WindowsServiceName" />,
    ///     "DraftModelLoader" by default) with
    ///     <c>sc failure DraftModelLoader reset=86400 actions=restart/5000/restart/10000/restart/30000</c>
    ///     already configured — SCM only runs those recovery actions on abnormal termination, not on
    ///     a graceful <c>sc stop</c>, so shelling out to stop/start ourselves would both duplicate
    ///     that existing schedule and bypass it (a manual stop doesn't count as a "failure").
    ///     Exiting non-zero is what SCM is already watching for.
    /// </summary>
    private void RestartWindowsServiceOrExitProcess()
    {
        LogHealthFailureExiting(logger, MaxConsecutiveUnhealthyCyclesBeforeRestart, string.IsNullOrWhiteSpace(config.Global.WindowsServiceName) ? "(unnamed)" : config.Global.WindowsServiceName);

        Environment.Exit(1);
    }

    /// <summary>
    ///     Records that a request handler threw an unhandled exception, so subsequent health
    ///     checks report degraded status even if Ollama and the models themselves still probe as
    ///     healthy. Only the exception's message is retained (not the full stack trace) since this
    ///     value can be surfaced in the <c>/health</c> response body to external callers — the
    ///     full exception is expected to already have been logged by the caller.
    /// </summary>
    public void RecordFault(Exception ex)
    {
        lock (healLock)
        {
            lastFaultMessage = ex.Message;
            lastFaultAtUtc = DateTimeOffset.UtcNow.UtcDateTime;
        }
    }

    /// <summary>True if a request handler threw an unhandled exception within the last <see cref="FaultTtl" />.</summary>
    public bool HasRecentFault
    {
        get
        {
            lock (healLock)
            {
                return lastFaultMessage is not null &&
                       DateTimeOffset.UtcNow.UtcDateTime - lastFaultAtUtc < FaultTtl;
            }
        }
    }

    /// <summary>
    ///     Gets information about currently loaded models (names, placement, memory usage).
    ///     Returns null if no models have been loaded yet.
    /// </summary>
    public LoadedModelsInfo? GetLoadedModelsInfo()
    {
        lock (healLock)
        {
            return loadedModelsInfo?.CreateSnapshot();
        }
    }

    /// <summary>
    ///     Updates the loaded models information. Called by the model loader/initializer
    ///     after models are successfully loaded.
    /// </summary>
    public void UpdateLoadedModels(string? draftName, string? mainName, string? embedName,
        bool draftOnGpu, bool mainOnGpu, bool embedOnGpu, double estimatedMemoryGb, int configurationId)
    {
        lock (healLock)
        {
            loadedModelsInfo = new LoadedModelsInfo
            {
                DraftModelName = draftName,
                MainModelName = mainName,
                EmbedModelName = embedName,
                DraftOnGpu = draftOnGpu,
                MainOnGpu = mainOnGpu,
                EmbedOnGpu = embedOnGpu,
                EstimatedMemoryGb = estimatedMemoryGb,
                ConfigurationId = configurationId,
                LoadedAt = DateTime.UtcNow,
            };
        }

        LogModelsUpdated(logger,
            draftName ?? "(none)", draftOnGpu ? "GPU" : "CPU",
            mainName ?? "(none)", mainOnGpu ? "GPU" : "CPU",
            embedName ?? "(none)", embedOnGpu ? "GPU" : "CPU",
            estimatedMemoryGb);
    }

    /// <summary>The message of the most recent recorded fault, or null if none is recent (see <see cref="HasRecentFault" />).</summary>
    public string? LastFaultMessage
    {
        get
        {
            lock (healLock)
            {
                return HasRecentFault ? lastFaultMessage : null;
            }
        }
    }

    /// <summary>Clears any recorded fault immediately — used by tests; not called from production code paths.</summary>
    public void ClearFault()
    {
        lock (healLock)
        {
            lastFaultMessage = null;
        }
    }

    /// <summary>
    ///     Runs the periodic background health/warmup loop until <paramref name="ct" /> is
    ///     cancelled. <paramref name="warmupModels" /> is evaluated on every iteration so the
    ///     caller can change which models are warmed (e.g. after an agent switch) without
    ///     restarting the loop.
    /// </summary>
    public async Task RunAsync(Func<IEnumerable<string>> warmupModels, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HealthCheckInterval, ct);
                await EnsureOllamaHealthyAsync();
                List<string> models = warmupModels().ToList();
                await WarmupModelsAsync(models);
                await EvaluatePeriodicHealthAsync(models);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogHealthCheckError(logger, ex, ex.Message);
            }
        }
    }

    /// <summary>
    ///     Called once per <see cref="RunAsync" /> cycle, after that cycle's own
    ///     <see cref="EnsureOllamaHealthyAsync" />/<see cref="WarmupModelsAsync" /> recovery
    ///     attempts, to decide whether the process is still unhealthy and — if it has been for
    ///     <see cref="MaxConsecutiveUnhealthyCyclesBeforeRestart" /> consecutive cycles — escalate
    ///     to a full restart via <see cref="RequestProcessRestart" />. Exposed separately from
    ///     <see cref="RunAsync" /> (rather than inlined) so tests can drive the escalation counter
    ///     directly without waiting through real <see cref="HealthCheckInterval" /> delays.
    /// </summary>
    internal async Task EvaluatePeriodicHealthAsync(IEnumerable<string> models)
    {
        bool healthy = await process.IsOllamaReadyAsync();
        if (healthy)
        {
            foreach (string model in models)
            {
                // A model with a generation running is serving; probing it would queue behind that
                // generation and time out, counting a busy model as unhealthy.
                if (inFlight?.IsBusy(model) == true)
                {
                    continue;
                }

                if (!await process.IsModelResponsiveAsync(model))
                {
                    healthy = false;
                    break;
                }
            }
        }

        int cyclesToReport = 0;
        bool shouldRestart = false;
        lock (healLock)
        {
            if (healthy)
            {
                consecutiveUnhealthyCycles = 0;
            }
            else
            {
                consecutiveUnhealthyCycles++;
                cyclesToReport = consecutiveUnhealthyCycles;
                if (consecutiveUnhealthyCycles >= MaxConsecutiveUnhealthyCyclesBeforeRestart)
                {
                    consecutiveUnhealthyCycles = 0;
                    shouldRestart = true;
                }
            }
        }

        if (shouldRestart)
        {
            LogRecoveryFailed(logger, $"Health has failed {cyclesToReport} consecutive cycles (~{cyclesToReport * HealthCheckInterval.TotalSeconds}s) despite in-cycle recovery attempts. Escalating to full process restart.");
            (RequestProcessRestart ?? RestartWindowsServiceOrExitProcess).Invoke();
        }
    }

    /// <summary>
    ///     Keeps the given models loaded in VRAM by calling them periodically. This prevents the
    ///     1+ second load time on the first request after keep_alive expires.
    /// </summary>
    public async Task WarmupModelsAsync(IEnumerable<string> models)
    {
        // Warm concurrently rather than one-at-a-time. Each WarmupModelAsync can block up to
        // MaxRetries * ModelWarmupTimeout (~60s) when Ollama is wedged, so a sequential loop over
        // draft/main/embed made a single RunAsync health cycle take minutes — which in turn
        // starved EvaluatePeriodicHealthAsync (and its escalation-to-restart counter) of the
        // regular cadence it needs to react. Running them together bounds a cycle's warmup cost to
        // the slowest single model instead of their sum. WarmupModelAsync already guards its shared
        // state under healLock and is independent per model, so this is safe to parallelize.
        await Task.WhenAll(models.Select(WarmupModelAsync));
    }

    public async Task WarmupModelAsync(string model)
    {
        const int MaxRetries = 2;
        const int FailureThresholdForRecovery = 3;
        int attemptNumber = 0;

        // A model already resident in VRAM is warm. Warming it anyway sends a generation that queues
        // behind any real generation on that model; the warming flag then stays set for the whole
        // wait and every request is answered warming_up. Only warm models that are not loaded.
        if (await process.IsModelLoadedInVramAsync(model))
        {
            return;
        }

        try
        {
            lock (healLock)
            {
                _ = warmingUpModels.Add(model);
            }

            while (attemptNumber < MaxRetries)
            {
                attemptNumber++;
                if (await AttemptWarmupAsync(model))
                {
                    lock (healLock)
                    {
                        modelLastActive[model] = DateTimeOffset.UtcNow.DateTime;
                        modelWarmupFailures.Remove(model);
                    }
                    return;
                }

                if (attemptNumber < MaxRetries)
                {
                    await Task.Delay(500);
                }
            }

            int failureCount = 0;
            lock (healLock)
            {
                modelWarmupFailures.TryGetValue(model, out failureCount);
                modelWarmupFailures[model] = failureCount + 1;
            }

            LogWarmupFailed(logger, model, attemptNumber);

            if (failureCount + 1 >= FailureThresholdForRecovery)
            {
                // If Ollama still answers /api/tags but warmups keep failing, the process is
                // wedged (scheduler up, GPU runner never loads) — a plain EnsureOllamaHealthyAsync()
                // would no-op on the passing readiness probe, so force a hard kill+restart. When the
                // probe itself is failing, the normal (unforced) recovery already handles it.
                bool wedged = await process.IsOllamaReadyAsync();
                string reason = wedged
                    ? "Ollama answers probes but can't load models — forcing a hard Ollama restart."
                    : "Triggering Ollama recovery.";
                LogWarmupAttemptFailed(logger, model, reason);
                await EnsureOllamaHealthyAsync(force: wedged);

                // Reset this model's tally so the restart gets FailureThresholdForRecovery fresh
                // cycles to take effect instead of forcing another kill+restart on every subsequent
                // failed cycle (a restart loop). A successful warmup clears it at the top of the loop.
                lock (healLock)
                {
                    _ = modelWarmupFailures.Remove(model);
                }
            }
        }
        catch (Exception ex)
        {
            LogWarmupError(logger, ex, model, attemptNumber, ex.GetType().Name);
        }
        finally
        {
            lock (healLock)
            {
                _ = warmingUpModels.Remove(model);
            }
        }
    }

    private async Task<bool> AttemptWarmupAsync(string model)
    {
        if (config.Global.Provider.Type != Provider.Ollama)
        {
            // LM Studio gets health-check-only treatment (see LMStudioProcessManagerAdapter) — no
            // keep_alive/warmup control exists for it, so there's nothing to warm up. Treating it
            // as "always warm" avoids every warmup cycle 404ing against LM Studio's OpenAI-only
            // surface and climbing modelWarmupFailures toward a spurious forced-recovery cycle.
            return true;
        }

        try
        {
            bool isEmbed = config.IsEmbedModel(model);
            JsonObject requestBody = BuildWarmupRequestBody(model, "x");

            using JsonContent request = JsonContent.Create(requestBody);
            using CancellationTokenSource cts = new(ModelWarmupTimeout);
            Uri endpoint = new($"http://127.0.0.1:{process.ServerPort}/{(isEmbed ? "api/embed" : "api/generate")}");
            using HttpResponseMessage response = await http.PostAsync(endpoint, request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            LogWarmupNetworkError(logger, ex, model);
            return false;
        }
        catch (TaskCanceledException)
        {
            // A warmup POST that times out isn't necessarily a failure. The very first inference
            // after a cold Ollama start spends a one-time stretch compiling GPU kernels (~50s
            // observed on a GTX 1660) even though the model's weights loaded into VRAM within a
            // second or two — so the POST can exceed ModelWarmupTimeout while the model is, in
            // fact, loaded and warming. If /api/ps already reports it resident, treat that as
            // warmed rather than a failure: counting it as a failure is what drove the repeated
            // "warmup failed" churn, and (now that recovery can force a restart) would kill a
            // model mid-warmup and restart the same slow cold load in a loop. A genuinely wedged
            // Ollama never becomes resident, so those still register as failures.
            if (await process.IsModelLoadedInVramAsync(model))
            {
                LogWarmupResponse(logger, model, 200);
                return true;
            }

            LogWarmupTimeout(logger, model);
            return false;
        }
    }

    private JsonObject BuildWarmupRequestBody(string model, string promptText)
    {
        string keepAlive = config.ModelSources.TryGetValue(model, out IModelSourceConfig? src) && src.KeepAlive is not null
            ? src.KeepAlive
            : "5m";
        bool isEmbed = config.IsEmbedModel(model);
        JsonObject requestBody = isEmbed
            ? new JsonObject { ["model"] = model, ["input"] = promptText, ["keep_alive"] = keepAlive }
            : new JsonObject { ["model"] = model, ["prompt"] = promptText, ["stream"] = false, ["keep_alive"] = keepAlive };
        // Warm-up only needs the model resident, so cap a chat model's generation to one token.
        // Without the cap the model writes a full reply, which can run to the warm-up timeout.
        JsonObject options = new();
        if (!isEmbed)
        {
            options["num_predict"] = 1;
        }

        int? numGpu = gpuLayersForModel(model);
        if (numGpu is not null)
        {
            options["num_gpu"] = numGpu.Value;
        }

        if (options.Count > 0)
        {
            requestBody["options"] = options;
        }

        return requestBody;
    }

    public bool IsModelWarmingUp(string model)
    {
        lock (healLock)
        {
            return warmingUpModels.Contains(model);
        }
    }

    /// <summary>
    ///     Test seam for the actual Ollama kill+restart performed by
    ///     <see cref="EnsureOllamaHealthyAsync" />. Null (the default) means "use the real
    ///     <see cref="OllamaProcessManager.KillStuckOllama" />/<see cref="OllamaProcessManager.StartServerAsync" />
    ///     path". Unit tests set this to observe that a recovery was requested (and whether it was
    ///     forced) without killing the developer's real Ollama processes — the same pattern
    ///     <see cref="RequestProcessRestart" /> uses for the escalation path. The bool argument is
    ///     <see langword="true" /> when the recovery was forced (Ollama answers probes but models
    ///     won't load), <see langword="false" /> for an ordinary unreachable-Ollama recovery.
    /// </summary>
    internal Func<bool, Task>? OllamaRecoveryOverride { get; set; }

    /// <summary>
    ///     Ensures Ollama is healthy, recovering it (kill + restart) if not.
    ///     <paramref name="force" /> skips the <see cref="OllamaProcessManager.IsOllamaReadyAsync" />
    ///     short-circuit: it's for the "wedged but answering" case where <c>/api/tags</c> still
    ///     returns 200 (so the normal guard would no-op) but the scheduler never loads a model, so
    ///     only a hard kill+restart of the process clears it. Note that even a full router-process
    ///     restart does NOT fix that case on its own — <see cref="SimpleModelPairApp" />'s startup attaches
    ///     to any Ollama that answers <c>/api/tags</c> rather than restarting it — which is why the
    ///     forced kill+restart has to happen here.
    /// </summary>
    public async Task EnsureOllamaHealthyAsync(bool force = false)
    {
        if (!force && await process.IsOllamaReadyAsync())
        {
            return;
        }

        lock (healLock)
        {
            if (recoveryInProgress)
            {
                return;
            }

            recoveryInProgress = true;
        }

        try
        {
            if (OllamaRecoveryOverride is not null)
            {
                await OllamaRecoveryOverride(force);
                return;
            }

            string reason = force
                ? "answering probes but models won't load — forcing a hard restart"
                : "unhealthy/unreachable — attempting restart";
            LogOllamaRecoveryStarting(logger, reason);
            process.KillStuckOllama();
            await Task.Delay(RecoverySettleDelay);
            if (await process.StartServerAsync())
            {
                if (OnRecovered is not null)
                {
                    await OnRecovered();
                }

                LogOllamaRecoveryComplete(logger);
            }
        }
        finally
        {
            lock (healLock)
            {
                recoveryInProgress = false;
            }
        }
    }

    /// <summary>
    ///     Generates a complete health report including GPU info, loaded models, and recent faults.
    /// </summary>
    public HealthReportData GenerateHealthReport(GlobalSettings globalSettings)
    {
        var loadedModels = GetLoadedModelsInfo();
        var gpu = globalSettings.GpuInfo;

        var report = new HealthReportData
        {
            ConfigurationId = globalSettings.ConfigurationId,
            Models = loadedModels,
            Gpu = HealthReportData.FromGpuInfo(gpu),
            RecentFault = LastFaultMessage,
            Timestamp = DateTime.UtcNow,
        };

        report.Status = report.GetHealthSummary();
        report.Message = loadedModels?.GetStatus() ?? "No models loaded";

        return report;
    }

    public async Task EnsureModelResponsiveAsync(string model)
    {
        if (config.Global.Provider.Type != Provider.Ollama)
        {
            // No process control for LM Studio — see AttemptWarmupAsync's guard for why.
            return;
        }

        if (await process.IsModelResponsiveAsync(model))
        {
            return;
        }

        LogModelUnresponsive(logger, model);
        await process.UnloadModelAsync(model);

        try
        {
            bool isEmbed = config.IsEmbedModel(model);
            JsonObject requestBody = BuildWarmupRequestBody(model, "hi");

            using JsonContent request = JsonContent.Create(requestBody);
            using CancellationTokenSource cts = new(ModelReloadTimeout);
            Uri endpoint = new($"http://127.0.0.1:{process.ServerPort}/{(isEmbed ? "api/embed" : "api/generate")}");
            using HttpResponseMessage response = await http.PostAsync(endpoint, request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                LogModelReloadedSuccessfully(logger, model);
            }
            else
            {
                LogModelReloadReturned(logger, model, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            LogModelReloadFailed(logger, ex, model, ex.Message);
        }
    }

}


