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

internal sealed partial class ModelHealthMonitor
{
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
		int? numGpu = gpuLayersForModel(model);
		if (numGpu is not null)
		{
			requestBody["options"] = new JsonObject { ["num_gpu"] = numGpu.Value };
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

}
