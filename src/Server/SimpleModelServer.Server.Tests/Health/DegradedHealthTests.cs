using System.Net;
using System.Text;
using System.Text.Json;

using Xunit;
using Xunit.Sdk;

namespace SimpleModelServer.Server.Tests;

/// <summary>
///     Covers the two ways /health can report degraded status even when Ollama and both models
///     probe as healthy: a recent unhandled request-handler exception (see
///     ModelHealthMonitor.RecordFault), and a failing endpoint self-test check (see
///     RouterHttpServer.RunSelfTests). Both are documented in docs/router-contract.md under
///     "Fault tracking" and "Endpoint self-tests".
/// </summary>
public sealed class DegradedHealthTests
{
    [Fact]
    public async Task UnhandledExceptionInHandler_MakesHealthDegraded()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        // Sanity check: healthy before anything goes wrong.
        using (HttpResponseMessage before = await fixture.Client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        // Malformed JSON body reaches JsonNode.Parse(body) inside HandleOllamaGenerateAsync
        // uncaught, propagates through RoutePostAsync into HandleRequestAsync's catch-all, which
        // is exactly the router's own unhandled-exception path (not a test-only injection hook).
        using HttpResponseMessage generateResponse = await fixture.Client.PostAsync(
            new Uri("/api/generate", UriKind.Relative), new StringContent("this is not json", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, generateResponse.StatusCode);

        using HttpResponseMessage healthResponse = await fixture.Client.GetAsync("/health", TestContext.Current.CancellationToken);
        string body = await healthResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, healthResponse.StatusCode);
        Assert.Equal("degraded", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("healthy", doc.RootElement.GetProperty("ollama").GetString());
        string? lastError = doc.RootElement.GetProperty("lastError").GetString();
        Assert.False(string.IsNullOrEmpty(lastError));

        // /health/ready shares the same fault signal (though not the self-test suite).
        using HttpResponseMessage readyResponse = await fixture.Client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        JsonDocument readyDoc = JsonDocument.Parse(await readyResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.Equal("degraded", readyDoc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task FailingSelfTestCheck_MakesHealthDegraded_WithoutAffectingHealthReady()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Router.ExtraSelfTestChecks.Add(("always-fails", () => false));

        using HttpResponseMessage healthResponse = await fixture.Client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        string body = await healthResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, healthResponse.StatusCode);
        Assert.Equal("degraded", doc.RootElement.GetProperty("status").GetString());
        JsonElement selfTest = doc.RootElement.GetProperty("selfTest");
        Assert.False(selfTest.GetProperty("ok").GetBoolean());

        bool foundFailingCheck = false;
        foreach (JsonElement check in selfTest.GetProperty("checks").EnumerateArray())
        {
            if (check.GetProperty("name").GetString() == "always-fails")
            {
                foundFailingCheck = true;
                Assert.False(check.GetProperty("ok").GetBoolean());
            }
        }

        Assert.True(foundFailingCheck);

        // /health/ready deliberately doesn't run self-tests (kept cheap for frequent polling —
        // see docs/router-contract.md), so it should still report healthy.
        using HttpResponseMessage readyResponse = await fixture.Client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
    }

    [Fact]
    public async Task SelfTestCheck_ThrowingException_IsCaughtAndReportedAsFailure()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Router.ExtraSelfTestChecks.Add(("throws", () => throw new InvalidOperationException("boom")));

        using HttpResponseMessage healthResponse = await fixture.Client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, healthResponse.StatusCode);
        JsonElement selfTest = doc.RootElement.GetProperty("selfTest");
        Assert.False(selfTest.GetProperty("ok").GetBoolean());

        bool foundThrowingCheck = false;
        foreach (JsonElement check in selfTest.GetProperty("checks").EnumerateArray())
        {
            if (check.GetProperty("name").GetString() == "throws")
            {
                foundThrowingCheck = true;
                Assert.False(check.GetProperty("ok").GetBoolean());
                Assert.Equal("boom", check.GetProperty("error").GetString());
            }
        }

        Assert.True(foundThrowingCheck);

        // Crucially: a throwing self-test check must not itself surface as a 500 to the /health
        // caller — /health should always come back with a well-formed degraded response.
        Assert.True(healthResponse.IsSuccessStatusCode is false && (int)healthResponse.StatusCode == 503);
    }

    [Fact]
    public async Task Health_AllHealthy_IncludesPassingSelfTestSuite()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync("/health", TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement selfTest = doc.RootElement.GetProperty("selfTest");
        Assert.True(selfTest.GetProperty("ok").GetBoolean());
        Assert.True(selfTest.GetProperty("checks").GetArrayLength() >= 3);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("lastError").ValueKind);
    }

    /// <summary>
    ///     This is the "model(s) available, but none are currently loaded in VRAM" scenario seen in
    ///     production /health polling: Ollama itself is up (probes on /api/tags succeed) but a
    ///     model has been evicted from VRAM (or never warmed), so /api/generate against it fails.
    ///     Before this test, <see cref="FakeOllamaServer.FailGenerate" /> was never exercised by
    ///     any test in this project, so this exact degrade path had zero coverage.
    ///     RecoverUnhealthyAsync attempts one reload via
    ///     ModelHealthMonitor.EnsureModelResponsiveAsync, which itself hits the still-failing fake
    ///     generate endpoint and gives up, so the model stays unresponsive and /health should
    ///     report it as such rather than silently reporting "ok".
    /// </summary>
    [Fact]
    public async Task Health_ModelUnresponsive_ReportsUnhealthyModelNotWarmingUp()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.FailGenerate = true;

        using HttpResponseMessage healthResponse = await fixture.Client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, healthResponse.StatusCode);
        // Ollama itself is reachable (only /api/generate is broken), so this is a genuine
        // "model down" case rather than the fault/self-test-only "degraded" case.
        Assert.Equal("unhealthy", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("healthy", doc.RootElement.GetProperty("ollama").GetString());
        JsonElement models = doc.RootElement.GetProperty("models");
        Assert.Equal("unhealthy", models.GetProperty("draft").GetString());
        Assert.Equal("unhealthy", models.GetProperty("main").GetString());

        // /health/ready must reflect the same underlying model outage.
        using HttpResponseMessage readyResponse = await fixture.Client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        JsonDocument readyDoc = JsonDocument.Parse(await readyResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.Equal("unhealthy", readyDoc.RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    ///     Same underlying condition as above (model not loaded), but observed through
    ///     POST /api/generate instead of /health: RouterHttpServer.CheckModelWarmupAsync must
    ///     detect the unresponsive model up front and return 202 "warming_up" rather than letting
    ///     the request fall through to a hard failure.
    /// </summary>
    [Fact]
    public async Task ApiGenerate_ModelNotLoaded_Returns202WarmingUp()
    {
        await using RouterTestFixture fixture = await RouterTestFixture.CreateAsync();
        fixture.Ollama.FailGenerate = true;

        using HttpResponseMessage response = await fixture.Client.PostAsync(
            new Uri("/api/generate", UriKind.Relative), new StringContent("""{"prompt":"hi","stream":false}""", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal((HttpStatusCode)202, response.StatusCode);
        Assert.Equal("warming_up", doc.RootElement.GetProperty("status").GetString());
    }
}
