using System.Net;
using System.Text.Json;

using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Server.Infrastructure.Networking;

using Xunit;
using Xunit.Sdk;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers the trusted-network gate that runs before every request is routed (see
///     docs/router-contract.md "Cross-cutting behavior"). Loopback callers (which is all of these
///     tests, since the fixture binds 127.0.0.1) must always be let through regardless of a
///     restrictive trustedNetworks list, since that's the documented "never lock out localhost"
///     guarantee.
/// </summary>
public sealed class TrustedNetworkTests
{
    [Fact]
    public async Task LoopbackCaller_AlwaysAllowed_EvenWithRestrictiveTrustedNetworks()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync(config => new AppConfig
        {
            Global = new GlobalConfig
            {
                DefaultAgentType = config.Global.DefaultAgentType,
                OllamaPort = config.Global.OllamaPort,
                RouterHost = config.Global.RouterHost,
                RouterModelName = config.Global.RouterModelName,
                TrustedNetworks = ["203.0.113.0"]
            },
            Agents = config.Agents
        });

        using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnrestrictedConfig_AllowsAnyClient()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Forbidden_Response_HasExpectedShape_WhenSimulatedViaDirectTrustedNetworkList()
    {
        // The router only ever sees loopback traffic in-process (HttpListener on 127.0.0.1), so
        // the 403 path itself can't be reached end-to-end from this test process. Instead this
        // asserts the underlying TrustedNetworkList primitive the router gates on, matching the
        // documented behavior: a non-loopback address not in trustedNetworks is rejected.
        TrustedNetworkList list =
            TrustedNetworkList.FromConfig(["203.0.113.0"]);

        Assert.False(list.IsAllowed(IPAddress.Parse("198.51.100.5")));
        Assert.True(list.IsAllowed(IPAddress.Parse("203.0.113.42")));
        Assert.True(list.IsAllowed(IPAddress.Loopback));
    }
}
