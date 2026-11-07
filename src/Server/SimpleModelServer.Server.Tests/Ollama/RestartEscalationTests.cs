using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers ModelHealthMonitor.EvaluatePeriodicHealthAsync's escalation-to-restart behavior: when
///     the periodic health cycle (RunAsync) finds Ollama or a warmup model still unhealthy for
///     MaxConsecutiveUnhealthyCyclesBeforeRestart consecutive cycles — despite that cycle's own
///     Ollama-recovery attempt — it must invoke RequestProcessRestart exactly once and reset the
///     counter, rather than sitting degraded indefinitely (the "still Degraded" complaint this was
///     added for) or restarting on every cycle once the threshold is crossed.
/// </summary>
public sealed class RestartEscalationTests
{
	[Fact]
	public async Task ConsecutiveUnhealthyCycles_BelowThreshold_DoesNotRestart()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
		fixture.Ollama.FailGenerate = true;

		int restartCount = 0;
		fixture.Health.RequestProcessRestart = () => restartCount++;

		// Threshold is 4 consecutive cycles — 3 should stay under it.
		for (int i = 0; i < 3; i++)
		{
			await fixture.Health.EvaluatePeriodicHealthAsync([fixture.Config.Global.DraftModel, fixture.Config.Global.MainModel]);
		}

		Assert.Equal(0, restartCount);
	}

	[Fact]
	public async Task ConsecutiveUnhealthyCycles_ReachingThreshold_TriggersRestartExactlyOnce()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
		fixture.Ollama.FailGenerate = true;

		int restartCount = 0;
		fixture.Health.RequestProcessRestart = () => restartCount++;

		string[] models = [fixture.Config.Global.DraftModel, fixture.Config.Global.MainModel];

		// 4th consecutive unhealthy cycle should cross the threshold.
		for (int i = 0; i < 4; i++)
		{
			await fixture.Health.EvaluatePeriodicHealthAsync(models);
		}

		Assert.Equal(1, restartCount);

		// The counter resets after firing, so more unhealthy cycles shouldn't fire again until
		// another full threshold's worth have passed.
		for (int i = 0; i < 3; i++)
		{
			await fixture.Health.EvaluatePeriodicHealthAsync(models);
		}

		Assert.Equal(1, restartCount);
	}

	[Fact]
	public async Task HealthyCycle_ResetsConsecutiveFailureCount()
	{
		await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

		int restartCount = 0;
		fixture.Health.RequestProcessRestart = () => restartCount++;

		string[] models = [fixture.Config.Global.DraftModel, fixture.Config.Global.MainModel];

		fixture.Ollama.FailGenerate = true;
		for (int i = 0; i < 3; i++)
		{
			await fixture.Health.EvaluatePeriodicHealthAsync(models);
		}

		// Recovers before hitting the threshold — counter should reset rather than carry over.
		fixture.Ollama.FailGenerate = false;
		await fixture.Health.EvaluatePeriodicHealthAsync(models);

		fixture.Ollama.FailGenerate = true;
		for (int i = 0; i < 3; i++)
		{
			await fixture.Health.EvaluatePeriodicHealthAsync(models);
		}

		Assert.Equal(0, restartCount);
	}
}
