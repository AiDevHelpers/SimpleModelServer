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

	/// <summary>Clears any recorded fault immediately — used by tests; not called from production code paths.</summary>
	public void ClearFault()
	{
		lock (healLock)
		{
			lastFaultMessage = null;
		}
	}

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
