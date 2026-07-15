using System.Collections.Generic;
using System.Threading.Tasks;

using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Dtos.Providers;
using SimpleModelServer.Contracts.Providers;

using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers the "health-check only, no process control" guard added to
///     <see cref="ModelHealthMonitor.AttemptWarmupAsync" />/<see cref="ModelHealthMonitor.EnsureModelResponsiveAsync" />
///     for Provider=LMStudio: those methods bypass <c>IOllamaProcessManager</c> entirely and POST
///     raw Ollama-native JSON directly, so without the guard they would 404/format-mismatch
///     against LM Studio every warmup cycle and eventually trigger a spurious forced-recovery
///     cycle (see <see cref="WedgedOllamaRecoveryTests" /> for the Ollama-mode equivalent this
///     guard must NOT trigger under LM Studio).
/// </summary>
public sealed class LMStudioHealthGuardTests
{
    [Fact]
    public async Task WarmupModelAsync_ProviderIsLMStudio_NeverEscalatesEvenWhenBackendUnreachable()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync(config => new AppConfig
        {
            Global = new GlobalConfig
            {
                DefaultAgentType = config.Global.DefaultAgentType,
                OllamaPort = config.Global.OllamaPort,
                RouterHost = config.Global.RouterHost,
                RouterModelName = config.Global.RouterModelName,
                Provider = new ProviderConfiguration { Type = Provider.LMStudio }
            },
            Agents = config.Agents
        });

        // Even with the fake backend fully failing, the Provider=LMStudio guard should short-
        // circuit AttemptWarmupAsync to "always warm" before it ever reaches the Ollama-native
        // HTTP call that would otherwise fail against this fake.
        fixture.Ollama.FailGenerate = true;

        List<bool> recoveries = [];
        fixture.Health.OllamaRecoveryOverride = forced =>
        {
            recoveries.Add(forced);
            return Task.CompletedTask;
        };

        for (int i = 0; i < 5; i++)
        {
            await fixture.Health.WarmupModelAsync(fixture.Config.Global.DraftModel);
        }

        Assert.Empty(recoveries);
    }
}
