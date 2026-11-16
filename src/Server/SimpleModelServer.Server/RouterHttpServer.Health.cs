using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Hardware;
using SimpleModelServer.Mcp;
using SimpleModelServer.Providers.Ollama;
using SimpleModelServer.Server.Interfaces;
using SimpleModelServer.Server.Infrastructure.Networking;
using SimpleModelServer.Server.Configuration;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server;

internal sealed partial class RouterHttpServer
{
	/// <summary>
	///     Blocks until neither the draft nor main model is mid-reload — see
	///     <see cref="ModelReloadCoordinator" />. Unlike <see cref="CheckModelWarmupAsync" /> (which
	///     returns a 202 for the caller to retry), this actually waits: a reload is expected to
	///     resolve in seconds, and callers that already paused cooperatively via
	///     <see cref="ModelStatusHub" /> shouldn't need to poll.
	/// </summary>
	private Task WaitForModelsReadyAsync(CancellationToken ct)
		=> Task.WhenAll(
			reloadCoordinator.WaitIfPausedAsync(generation.DraftModelName, ct),
			reloadCoordinator.WaitIfPausedAsync(generation.MainModelName, ct));

	/// <summary>
	///     Checks if models are warming up and returns a 202 response if they are.
	///     Returns true if any model is warming up (caller should not proceed with request).
	/// </summary>
	private async Task<bool> CheckModelWarmupAsync(HttpContext context)
	{
		bool draftWarming = health.IsModelWarmingUp(generation.DraftModelName);
		bool mainWarming = health.IsModelWarmingUp(generation.MainModelName);

		if (!draftWarming && !await process.IsModelResponsiveAsync(generation.DraftModelName, ModelResponsiveWarmupCheckTimeout))
		{
			_ = health.WarmupModelAsync(generation.DraftModelName);
			draftWarming = true;
		}

		if (!mainWarming && !await process.IsModelResponsiveAsync(generation.MainModelName, ModelResponsiveWarmupCheckTimeout))
		{
			_ = health.WarmupModelAsync(generation.MainModelName);
			mainWarming = true;
		}

		if (draftWarming || mainWarming)
		{
			string message = draftWarming && mainWarming
				? "Models warming up. Please retry in 2-3 seconds."
				: draftWarming
					? "Draft model warming up. Please retry in 1-2 seconds."
					: "Main model warming up. Please retry in 2-3 seconds.";

			await WriteJsonAsync(context, 202, new
			{
				status = "warming_up",
				message,
				retry_after = draftWarming && mainWarming ? 3 : draftWarming ? 2 : 3
			});
			return true;
		}

		return false;
	}

	// ── Endpoint self-tests (part of GET /health only — see docs/router-contract.md) ──

	private readonly record struct SelfTestCheckResult(string Name, bool Ok, string? Error);

	private readonly record struct SelfTestResult(bool Ok, IReadOnlyList<SelfTestCheckResult> Checks);

	/// <summary>
	///     Extra checks appended to the built-in self-test suite. Empty in production; tests use
	///     this to inject a deliberately-failing check without needing to actually break a real
	///     endpoint, to verify the degraded-status wiring end to end.
	/// </summary>
	internal List<(string Name, Func<bool> Check)> ExtraSelfTestChecks { get; } = [];

	/// <summary>
	///     Exercises a handful of the router's own lightweight GET response builders in-process
	///     (no network round-trip — see BuildVersionBody/BuildTagsBody/BuildModelsListBody above)
	///     to catch a broken handler before a real caller does. Each check is independent: one
	///     throwing doesn't stop the rest from running.
	/// </summary>
	private SelfTestResult RunSelfTests()
	{
		List<(string Name, Func<bool> Check)> checks =
		[
			("api/version", () =>
			{
				object body = BuildVersionBody();
				JsonNode? json = JsonNode.Parse(JsonSerializer.Serialize(body));
				return !string.IsNullOrEmpty(json?["version"]?.GetValue<string>());
			}),
			("api/tags", () =>
			{
				object body = BuildTagsBody();
				JsonNode? json = JsonNode.Parse(JsonSerializer.Serialize(body));
				return json?["models"] is JsonArray { Count: > 0 };
			}),
			("v1/models", () =>
			{
				object body = BuildModelsListBody();
				JsonNode? json = JsonNode.Parse(JsonSerializer.Serialize(body));
				return string.Equals(json?["object"]?.GetValue<string>(), "list", StringComparison.Ordinal) &&
					   json?["data"] is JsonArray { Count: > 0 };
			}),
			.. ExtraSelfTestChecks
		];

		List<SelfTestCheckResult> results = new(checks.Count);
		bool allOk = true;
		foreach ((string name, Func<bool> check) in checks)
		{
			try
			{
				bool ok = check();
				results.Add(new SelfTestCheckResult(name, ok, ok ? null : "check returned false"));
				allOk &= ok;
			}
			catch (Exception ex)
			{
				results.Add(new SelfTestCheckResult(name, false, ex.Message));
				allOk = false;
			}
		}

		return new SelfTestResult(allOk, results);
	}

	/// <summary>
	///     Projects <see cref="SelfTestResult" /> to the lowercase-property anonymous shape used
	///     by every other JSON response in this file (anonymous-type property names are emitted
	///     verbatim by <see cref="JsonSerializer" /> with no configured naming policy, unlike this
	///     record's PascalCase members).
	/// </summary>
	private static object SelfTestBody(SelfTestResult result)
	{
		return new
		{
			ok = result.Ok,
			checks = result.Checks.Select(c => new { name = c.Name, ok = c.Ok, error = c.Error })
		};
	}

	private readonly record struct HealthSnapshot(
		bool OllamaHealthy, bool DraftHealthy, bool MainHealthy,
		bool DraftWarming, bool MainWarming, bool DraftIdle, bool MainIdle, string? FaultMessage)
	{
		/// <summary>
		///     True when Ollama and both models probe healthy AND no recent unhandled request
		///     exception has been recorded (see <see cref="ModelHealthMonitor.RecordFault" />) —
		///     a healthy process can still have just thrown a stack trace on some request, and
		///     that should surface as degraded even though the live probes alone would say ok.
		/// </summary>
		public bool AllHealthy => OllamaHealthy && DraftHealthy && MainHealthy && FaultMessage is null;
		public bool AnyWarming => DraftWarming || MainWarming;
	}

	/// <summary>
	///     Checks a model's health without letting a keep_alive-expired idle unload masquerade as
	///     unhealthy: if <see cref="OllamaProcessManager.IsModelLoadedInVramAsync" /> says the
	///     model isn't resident, that's expected idle behavior (not a fault) and reported healthy
	///     with <c>idle: true</c>, skipping the slow <see cref="OllamaProcessManager.IsModelResponsiveAsync(string)" />
	///     generate probe — which has to cold-load the model and can blow through its 3s timeout
	///     for a perfectly fine model, misreporting idle as unresponsive.
	/// </summary>
	private async Task<(bool Healthy, bool Idle)> ProbeModelHealthAsync(string model)
	{
		bool loaded = await process.IsModelLoadedInVramAsync(model);
		if (!loaded)
		{
			return (true, true);
		}

		return (await process.IsModelResponsiveAsync(model), false);
	}

	private async Task<HealthSnapshot> CaptureHealthSnapshotAsync()
	{
		bool ollamaHealthy = await process.IsOllamaReadyAsync();
		bool draftHealthy = true, mainHealthy = true, draftIdle = false, mainIdle = false;
		if (ollamaHealthy)
		{
			(draftHealthy, draftIdle) = await ProbeModelHealthAsync(generation.DraftModelName);
			(mainHealthy, mainIdle) = await ProbeModelHealthAsync(generation.MainModelName);
		}

		bool draftWarming = health.IsModelWarmingUp(generation.DraftModelName);
		bool mainWarming = health.IsModelWarmingUp(generation.MainModelName);
		string? faultMessage = health.LastFaultMessage;
		return new HealthSnapshot(
			ollamaHealthy, draftHealthy, mainHealthy, draftWarming, mainWarming, draftIdle, mainIdle, faultMessage);
	}

	private async Task<HealthSnapshot> RecoverUnhealthyAsync(HealthSnapshot snapshot)
	{
		if (snapshot.AllHealthy)
		{
			return snapshot;
		}

		// A recorded fault isn't something EnsureOllamaHealthyAsync/EnsureModelResponsiveAsync
		// can act on — it's a request-handler exception, not an Ollama/model outage — so there's
		// nothing to "recover" here beyond what the probes below already attempt. It stays
		// reflected in the re-captured snapshot until it ages out (see ModelHealthMonitor.FaultTtl).

		LogHealthCheckFailed(logger);
		if (!snapshot.OllamaHealthy)
		{
			await health.EnsureOllamaHealthyAsync();
		}

		if (!snapshot.DraftHealthy)
		{
			await health.EnsureModelResponsiveAsync(generation.DraftModelName);
		}

		if (!snapshot.MainHealthy)
		{
			await health.EnsureModelResponsiveAsync(generation.MainModelName);
		}

		return await CaptureHealthSnapshotAsync();
	}

	private static string WarmupMessage(HealthSnapshot s)
	{
		return s.DraftWarming && s.MainWarming
			? "Models warming up. Please retry in 2-3 seconds."
			: s.DraftWarming
				? "Draft model warming up. Please retry in 1-2 seconds."
				: "Main model warming up. Please retry in 2-3 seconds.";
	}

	private static string HealthState(bool healthy)
	{
		return healthy ? "healthy" : "unhealthy";
	}

	private static string ModelState(bool healthy, bool warming)
	{
		return healthy ? "healthy" : warming ? "warming_up" : "unhealthy";
	}

	/// <summary>
	///     Status string for the non-warming unhealthy case: "degraded" when Ollama/models are
	///     themselves fine and either a recorded request-handler fault or a failed self-test
	///     check is the only reason overall health is false, "unhealthy" when Ollama or a model
	///     is actually down.
	/// </summary>
	private static string UnhealthyStatus(HealthSnapshot snapshot, bool selfTestOk)
	{
		return snapshot.OllamaHealthy && snapshot.DraftHealthy && snapshot.MainHealthy &&
			   (snapshot.FaultMessage is not null || !selfTestOk)
			? "degraded"
			: "unhealthy";
	}

	private async Task HandleHealthAsync(HttpContext context)
	{
		HealthSnapshot snapshot = await CaptureHealthSnapshotAsync();
		snapshot = await RecoverUnhealthyAsync(snapshot);

		// Self-tests are in-process/no I/O, so running them unconditionally (even on the
		// warming-up path below) is cheap and keeps "healthy" meaning the same thing everywhere
		// /health reports it, rather than only checking it on the non-warming branch.
		SelfTestResult selfTest = RunSelfTests();
		bool allHealthy = snapshot.AllHealthy && selfTest.Ok;

		if (!allHealthy && snapshot.AnyWarming)
		{
			await WriteJsonAsync(context, 202, new
			{
				status = "warming_up",
				message = WarmupMessage(snapshot),
				retry_after = snapshot.DraftWarming && snapshot.MainWarming ? 3 : snapshot.DraftWarming ? 2 : 3,
				ollama = HealthState(snapshot.OllamaHealthy),
				models = new
				{
					draft = ModelState(snapshot.DraftHealthy, snapshot.DraftWarming),
					main = ModelState(snapshot.MainHealthy, snapshot.MainWarming)
				},
				agent = currentAgentType(),
				lastError = snapshot.FaultMessage,
				selfTest = SelfTestBody(selfTest)
			});
			return;
		}

		await WriteJsonAsync(context, allHealthy ? 200 : 503, new
		{
			status = allHealthy ? "ok" : UnhealthyStatus(snapshot, selfTest.Ok),
			ollama = HealthState(snapshot.OllamaHealthy),
			models = new
			{
				draft = HealthState(snapshot.DraftHealthy),
				main = HealthState(snapshot.MainHealthy)
			},
			// idle just means keep_alive expired and Ollama unloaded the model from VRAM — the
			// next request reloads it. Reported alongside the healthy status above, not as a
			// reason for it to be anything other than healthy (see ProbeModelHealthAsync).
			vram = new
			{
				draft = snapshot.DraftIdle ? "idle" : "loaded",
				main = snapshot.MainIdle ? "idle" : "loaded"
			},
			agent = currentAgentType(),
			lastError = snapshot.FaultMessage,
			selfTest = SelfTestBody(selfTest)
		});
	}

	private async Task HandleHealthReadyAsync(HttpContext context)
	{
		HealthSnapshot snapshot = await CaptureHealthSnapshotAsync();

		if (!snapshot.AllHealthy)
		{
			// /health/ready doesn't run self-tests (kept cheap for frequent polling — see
			// docs/router-contract.md), so selfTestOk is always true here: this path's status
			// can only reflect Ollama/model health or a recorded fault, never a self-test result.
			string status = snapshot.AnyWarming ? "warming_up" : UnhealthyStatus(snapshot, selfTestOk: true);
			await WriteJsonAsync(context, 503, new
			{
				status,
				ollama = HealthState(snapshot.OllamaHealthy),
				models = new
				{
					draft = ModelState(snapshot.DraftHealthy, snapshot.DraftWarming),
					main = ModelState(snapshot.MainHealthy, snapshot.MainWarming)
				},
				agent = currentAgentType(),
				lastError = snapshot.FaultMessage
			});
			return;
		}

		await WriteJsonAsync(context, 200, new
		{
			status = "ok",
			ollama = "healthy",
			models = new
			{
				draft = "healthy",
				main = "healthy"
			},
			agent = currentAgentType(),
			lastError = (string?)null
		});
	}

	/// <summary>
	///     Exposes resolved hardware settings so downstream tools (e.g. TokenSaver.MCP's
	///     workspace indexer) can size background-inference concurrency (AI file/folder
	///     summaries) off this machine's real capacity instead of guessing from CPU count.
	/// </summary>
	private async Task HandleCapacityAsync(HttpContext context)
	{
		ResolvedHardwareSettings hw = hardwareSettings();
		await WriteJsonAsync(context, 200, new
		{
			cpuThreads = hw.CpuThreads,
			gpuLayers = hw.GpuLayers,
			gpu = new
			{
				detected = hw.Gpu.Detected,
				name = hw.Gpu.Name,
				memoryMb = hw.Gpu.MemoryMb
			},
			maxConcurrentSummaryRequests = hw.MaxConcurrentSummaryRequests
		});
	}

	/// <summary>
	///     Exposes complete system information including capabilities, configuration, hardware,
	///     available models, and agent types. Useful for clients that need to discover system
	///     capabilities at runtime.
	/// </summary>
	private async Task HandleSystemInfoAsync(HttpContext context)
	{
		var systemInfo = systemInfoService.GetSystemInfo();
		await WriteJsonAsync(context, 200, systemInfo);
	}

}
