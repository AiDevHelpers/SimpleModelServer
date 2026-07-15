using System.Collections.Generic;
using System.Threading.Tasks;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers ModelHealthMonitor's warmup-failure recovery path for the "wedged but answering"
///     Ollama failure mode: the server keeps returning 200 to the /api/tags readiness probe, but
///     every /api/generate (or /api/embed) warmup hangs/fails because the scheduler never loads a
///     runner. Before this fix that state was unrecoverable — the threshold path called
///     EnsureOllamaHealthyAsync(), whose readiness-probe guard short-circuited on the passing
///     /api/tags, so "Triggering Ollama recovery" was logged but nothing was ever restarted. Now,
///     when the probe still passes, the recovery is *forced* (skips the guard) so the process
///     actually gets killed and restarted; when the probe itself is failing, the ordinary unforced
///     recovery still applies.
///
///     The actual kill+restart is stubbed via ModelHealthMonitor.OllamaRecoveryOverride so these
///     tests observe the decision (and whether it was forced) without killing the developer's real
///     Ollama processes — the same seam pattern RestartEscalationTests uses for RequestProcessRestart.
/// </summary>
public sealed class WedgedOllamaRecoveryTests
{
    [Fact]
    public async Task WarmupFailingWhileOllamaAnswersProbe_ForcesHardRestart()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        // /api/tags keeps answering 200 (FakeOllamaServer's default), but /api/generate fails —
        // the exact "wedged but answering" shape.
        fixture.Ollama.FailGenerate = true;

        List<bool> recoveries = [];
        fixture.Health.OllamaRecoveryOverride = forced =>
        {
            recoveries.Add(forced);
            return Task.CompletedTask;
        };

        // FailureThresholdForRecovery is 3, and each WarmupModelAsync call records one failure
        // (after its own internal retries), so three calls cross the threshold exactly once.
        for (int i = 0; i < 3; i++)
        {
            await fixture.Health.WarmupModelAsync(fixture.Config.Global.DraftModel);
        }

        Assert.Single(recoveries);
        Assert.True(recoveries[0], "Recovery should be FORCED when Ollama still answers /api/tags but warmups fail.");
    }

    [Fact]
    public async Task WarmupFailingWhileOllamaUnreachable_UsesUnforcedRecovery()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        List<bool> recoveries = [];
        fixture.Health.OllamaRecoveryOverride = forced =>
        {
            recoveries.Add(forced);
            return Task.CompletedTask;
        };

        // Take the backend fully down so both /api/tags and /api/generate fail — the ordinary
        // unreachable-Ollama case, which the normal (unforced) recovery already handled.
        fixture.Ollama.Dispose();

        for (int i = 0; i < 3; i++)
        {
            await fixture.Health.WarmupModelAsync(fixture.Config.Global.DraftModel);
        }

        Assert.Single(recoveries);
        Assert.False(recoveries[0], "Recovery should NOT be forced when the readiness probe itself is failing.");
    }

    [Fact]
    public async Task WarmupFailure_BelowThreshold_DoesNotTriggerRecovery()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.FailGenerate = true;

        List<bool> recoveries = [];
        fixture.Health.OllamaRecoveryOverride = forced =>
        {
            recoveries.Add(forced);
            return Task.CompletedTask;
        };

        // Two failures stay under the threshold of three.
        for (int i = 0; i < 2; i++)
        {
            await fixture.Health.WarmupModelAsync(fixture.Config.Global.DraftModel);
        }

        Assert.Empty(recoveries);
    }
}
