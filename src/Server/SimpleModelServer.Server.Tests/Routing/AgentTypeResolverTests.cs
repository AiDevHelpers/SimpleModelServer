using SimpleModelServer.Config.Configuration;

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using SimpleModelServer.Contracts.Agents;
using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers AgentTypeResolver.Resolve directly, in isolation from RouterHttpServer or any
///     generation logic — it's pure request-inspection logic and deserves to be tested as such.
///     Builds a real ASP.NET Core <see cref="HttpRequest" /> via <see cref="DefaultHttpContext" />
///     — unlike the old System.Net.HttpListenerRequest (which had no public constructor and
///     needed a real loopback socket to obtain one), HttpRequest is directly constructible for
///     tests, so no router/listener/socket is involved here at all.
/// </summary>
public sealed class AgentTypeResolverTests
{
    /// <summary>
    ///     Builds a request, applies <paramref name="configureRequest" />, and resolves it —
    ///     kept async (wrapping synchronous work in <see cref="Task.FromResult{TResult}" />) purely
    ///     so every existing "await ResolveAsync(...)" call site below didn't need to change.
    /// </summary>
    private static Task<string> ResolveAsync(
        AgentTypeResolver resolver,
        string pathAndQuery,
        JsonNode? body,
        string prompt,
        string currentAgentType,
        Action<HttpRequest>? configureRequest = null)
    {
        DefaultHttpContext httpContext = new();
        HttpRequest request = httpContext.Request;

        int queryIdx = pathAndQuery.IndexOf('?', StringComparison.Ordinal);
        request.Path = queryIdx >= 0 ? pathAndQuery[..queryIdx] : pathAndQuery;
        request.QueryString = queryIdx >= 0 ? new QueryString(pathAndQuery[queryIdx..]) : QueryString.Empty;

        configureRequest?.Invoke(request);

        return Task.FromResult(resolver.Resolve(request, body, prompt, currentAgentType));
    }

    private static AppConfig ConfigWithAgents(params string[] agentTypes)
    {
        Dictionary<string, IAgentConfig> agents = new(StringComparer.OrdinalIgnoreCase);
        foreach (string type in agentTypes)
        {
            agents[type] = new AgentConfig();
        }

        return new AppConfig { Agents = agents };
    }

    [Fact]
    public async Task Resolve_XAgentTypeHeader_TakesHighestPrecedence()
    {
        AgentTypeResolver resolver = new(ConfigWithAgents());

        string result = await ResolveAsync(resolver, "/", null, "irrelevant prompt", "CODE",
            req => req.Headers["X-Agent-Type"] = "debug");

        Assert.Equal("DEBUG", result);
    }

    [Fact]
    public async Task Resolve_PassthroughHeader_ReturnsCurrentAgentTypeUnchanged()
    {
        AgentTypeResolver resolver = new(ConfigWithAgents());

        string result = await ResolveAsync(resolver, "/", null, "this mentions PLANNING and DEBUG", "RESEARCH",
            req => req.Headers["X-Agent-Type"] = "passthrough");

        Assert.Equal("RESEARCH", result);
    }

    [Fact]
    public async Task Resolve_XCpChatAgentHeader_UsedWhenXAgentTypeAbsent()
    {
        AgentTypeResolver resolver = new(ConfigWithAgents());

        string result = await ResolveAsync(resolver, "/", null, "", "CODE",
            req => req.Headers["X-CP-Chat-Agent"] = "planning");

        Assert.Equal("PLANNING", result);
    }

    [Fact]
    public async Task Resolve_QueryStringAgentType_UsedWhenNoHeadersMatch()
    {
        AgentTypeResolver resolver = new(ConfigWithAgents());

        string result = await ResolveAsync(resolver, "/?agent_type=debug", null, "", "CODE");

        Assert.Equal("DEBUG", result);
    }

    [Fact]
    public async Task Resolve_HeaderTakesPrecedenceOverQueryString()
    {
        AgentTypeResolver resolver = new(ConfigWithAgents());

        string result = await ResolveAsync(resolver, "/?agent_type=planning", null, "", "CODE",
            req => req.Headers["X-Agent-Type"] = "debug");

        Assert.Equal("DEBUG", result);
    }

    [Fact]
    public async Task Resolve_BodyAgentTypeField_UsedWhenNoHeaderOrQueryMatch()
    {
        AgentTypeResolver resolver = new(ConfigWithAgents());
        JsonObject body = new() { ["agentType"] = "debug" };

        string result = await ResolveAsync(resolver, "/", body, "", "CODE");

        Assert.Equal("DEBUG", result);
    }

    [Fact]
    public async Task Resolve_BodyMetadataAgentTypeField_UsedAsFallback()
    {
        AgentTypeResolver resolver = new(ConfigWithAgents());
        JsonObject body = new() { ["metadata"] = new JsonObject { ["agent_type"] = "planning" } };

        string result = await ResolveAsync(resolver, "/", body, "", "CODE");

        Assert.Equal("PLANNING", result);
    }

    [Theory]
    [InlineData("my-model:debug")]
    [InlineData("my-model-debug")]
    [InlineData("vendor/debug")]
    public async Task Resolve_ModelNameSuffix_MapsToAgentType(string model)
    {
        AgentTypeResolver resolver = new(ConfigWithAgents());
        JsonObject body = new() { ["model"] = model };

        string result = await ResolveAsync(resolver, "/", body, "", "CODE");

        Assert.Equal("DEBUG", result);
    }

    [Fact]
    public async Task Resolve_SystemMessageWithAgentKeyword_IsUsedAsFallback()
    {
        AgentTypeResolver resolver = new(ConfigWithAgents());
        JsonObject body = new()
        {
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = "You are in DEBUG mode." },
                new JsonObject { ["role"] = "user", ["content"] = "hi" }
            }
        };

        string result = await ResolveAsync(resolver, "/", body, "", "CODE");

        Assert.Equal("DEBUG", result);
    }

    [Fact]
    public async Task Resolve_DeveloperRoleMessage_TreatedSameAsSystem()
    {
        AgentTypeResolver resolver = new(ConfigWithAgents());
        JsonObject body = new()
        {
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "developer", ["content"] = "PLANNING session" }
            }
        };

        string result = await ResolveAsync(resolver, "/", body, "", "CODE");

        Assert.Equal("PLANNING", result);
    }

    [Theory]
    [InlineData("search for the weather", "RESEARCH")]
    [InlineData("search the web for cats", "RESEARCH")]
    [InlineData("look up the docs", "RESEARCH")]
    [InlineData("find docs on xunit", "RESEARCH")]
    [InlineData("research: quantum computing", "RESEARCH")]
    [InlineData("let's do some debugging here", "DEBUG")]
    [InlineData("let's do some planning first", "PLANNING")]
    [InlineData("write some code please", "CODE")]
    public async Task Resolve_PromptKeywords_AreClassifiedAsLastResort(string prompt, string expected)
    {
        // "research" isn't one of AppConfig.IsKnownAgentType's hardcoded always-known types
        // (only debug/code/planning are) — it only resolves successfully when registered as a
        // configured agent, unlike the other three which work against an empty Agents config.
        AgentTypeResolver resolver = new(ConfigWithAgents("research"));

        string result = await ResolveAsync(resolver, "/", null, prompt, "CODE");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Resolve_KeywordMatch_RequiresWordBoundary()
    {
        // "decode" contains "code" as a substring but must NOT match — ContainsAgentWord's
        // boundary check is what this verifies.
        AgentTypeResolver resolver = new(ConfigWithAgents());

        string result = await ResolveAsync(resolver, "/", null, "please decode this string", "CODE");

        // No keyword should match ("decode" isn't a boundary-safe "code"), so it must fall
        // through everything to the configured default.
        Assert.Equal("CODE", result);
    }

    [Fact]
    public async Task Resolve_NoSignalsMatchAnything_FallsBackToConfiguredDefaultAgentType()
    {
        AppConfig config = new() { Global = new GlobalConfig { DefaultAgentType = "research" } };
        AgentTypeResolver resolver = new(config);

        string result = await ResolveAsync(resolver, "/", null, "totally unrelated text", "CODE");

        Assert.Equal("RESEARCH", result);
    }

    [Fact]
    public async Task Resolve_UnrecognizedCandidateValue_IsSkippedInFavorOfNextSignal()
    {
        // "agentType": "not-a-real-agent" isn't known, so Resolve must keep looking rather than
        // returning it verbatim — the prompt keyword should be reached next.
        AgentTypeResolver resolver = new(ConfigWithAgents());
        JsonObject body = new() { ["agentType"] = "not-a-real-agent" };

        string result = await ResolveAsync(resolver, "/", body, "help me debug this", "CODE");

        Assert.Equal("DEBUG", result);
    }

    [Fact]
    public async Task Resolve_ConfiguredCustomAgentType_IsRecognizedFromHeader()
    {
        AgentTypeResolver resolver = new(ConfigWithAgents("custom-agent"));

        string result = await ResolveAsync(resolver, "/", null, "", "CODE",
            req => req.Headers["X-Agent-Type"] = "custom-agent");

        Assert.Equal("CUSTOM-AGENT", result);
    }
}
