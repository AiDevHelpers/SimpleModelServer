using SimpleModelServer.Providers.Ollama;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;

namespace SimpleModelServer.Server;

/// <summary>
///     Coordinates a model reload/swap so in-flight and new requests pause instead of hitting a
///     model mid-unload: <see cref="BeginReloadAsync" /> closes a per-model gate, broadcasts a
///     pause over <see cref="ModelStatusHub" /> to any subscribed SignalR clients, waits (bounded
///     by <paramref name="ackTimeout" /> in the constructor) for those clients to ack, performs the
///     actual unload/reload via <see cref="IOllamaProcessManager" />, then reopens the gate and
///     broadcasts resume. <see cref="WaitIfPausedAsync" /> is the enforcement side — every request
///     handler awaits it regardless of whether the caller speaks SignalR at all, so the pause holds
///     even for plain HTTP clients that never subscribed to the hub.
/// </summary>
internal sealed partial class ModelReloadCoordinator(
	IOllamaProcessManager process,
	ModelHealthMonitor health,
	ILogger logger,
	TimeSpan ackTimeout)
{
	/// <summary>Per-model gate: a completed task means the model is open for requests.</summary>
	private readonly ConcurrentDictionary<string, TaskCompletionSource> gates =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> subscribersByModel =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly ConcurrentDictionary<string, ReloadRound> activeRounds =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Set once <see cref="RouterHttpServer.StartAsync" /> has built the Kestrel host and its
	///     DI container — <see cref="ModelReloadCoordinator" /> is constructed earlier (alongside
	///     the router's other dependencies), before a real <see cref="IHubContext{T}" /> exists.
	///     Null before that point (or in tests that construct this class directly): reloads still
	///     enforce the gate, they just skip the SignalR pause/ack/resume choreography.
	/// </summary>
	internal IHubContext<ModelStatusHub>? Hub { get; set; }

	/// <summary>Awaited by every generate/chat/completion handler before touching <paramref name="modelName" />.</summary>
	public Task WaitIfPausedAsync(string modelName, CancellationToken ct)
		=> gates.GetOrAdd(modelName, static _ => OpenGate()).Task.WaitAsync(ct);

	private static TaskCompletionSource OpenGate()
	{
		TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		tcs.SetResult();
		return tcs;
	}

	public void Subscribe(string modelName, string connectionId)
		=> subscribersByModel.GetOrAdd(modelName, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))[connectionId] = 0;

	public void Unsubscribe(string connectionId)
	{
		foreach (ConcurrentDictionary<string, byte> subscribers in subscribersByModel.Values)
		{
			subscribers.TryRemove(connectionId, out _);
		}
	}

	public void RecordAck(string modelName, string connectionId)
	{
		if (activeRounds.TryGetValue(modelName, out ReloadRound? round))
		{
			round.Ack(connectionId);
		}
	}

	/// <summary>
	///     Closes <paramref name="modelName" />'s gate, waits for subscribed clients to ack (or the
	///     ack timeout to elapse — a dropped/unresponsive client must never wedge a reload), swaps
	///     the model via <see cref="IOllamaProcessManager" />, then reopens the gate. The gate is
	///     always reopened even if the swap itself throws — a failed reload must not leave the
	///     model permanently paused.
	/// </summary>
	public async Task BeginReloadAsync(string modelName, CancellationToken ct = default)
	{
		TaskCompletionSource closedGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		gates[modelName] = closedGate;

		List<string> subscriberIds = subscribersByModel.TryGetValue(modelName, out ConcurrentDictionary<string, byte>? subs)
			? [.. subs.Keys]
			: [];

		ReloadRound round = new(subscriberIds.Count);
		activeRounds[modelName] = round;

		try
		{
			if (Hub is not null)
			{
				await Hub.Clients.Group(modelName).SendAsync("PauseForReload", modelName, ct);

				using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				timeoutCts.CancelAfter(ackTimeout);
				try
				{
					await round.AllAcked.Task.WaitAsync(timeoutCts.Token);
				}
				catch (OperationCanceledException) when (!ct.IsCancellationRequested)
				{
					LogAckTimeout(logger, modelName, subscriberIds.Count);
				}
			}

			await process.UnloadModelAsync(modelName);
			await process.EnsureModelAsync(modelName);
			await health.EnsureModelResponsiveAsync(modelName);
		}
		finally
		{
			activeRounds.TryRemove(modelName, out _);
			closedGate.SetResult();

			if (Hub is not null)
			{
				await Hub.Clients.Group(modelName).SendAsync("ResumeReady", modelName, CancellationToken.None);
			}
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Reload ack timeout for model '{ModelName}' — proceeding without all {SubscriberCount} subscriber(s) acking.")]
	private static partial void LogAckTimeout(ILogger logger, string modelName, int subscriberCount);

	/// <summary>Tracks acks for one in-flight reload round; completes once every subscriber has acked (or there were none).</summary>
	private sealed class ReloadRound
	{
		private readonly HashSet<string> acked = [];
		private readonly object sync = new();
		private int remaining;

		public ReloadRound(int expectedAcks)
		{
			remaining = expectedAcks;
			if (remaining <= 0)
			{
				AllAcked.TrySetResult();
			}
		}

		public TaskCompletionSource AllAcked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public void Ack(string connectionId)
		{
			lock (sync)
			{
				if (!acked.Add(connectionId))
				{
					return;
				}
			}

			if (Interlocked.Decrement(ref remaining) == 0)
			{
				AllAcked.TrySetResult();
			}
		}
	}
}
