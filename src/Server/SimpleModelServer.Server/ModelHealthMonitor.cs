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
	Func<string, int?> gpuLayersForModel)
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

}
