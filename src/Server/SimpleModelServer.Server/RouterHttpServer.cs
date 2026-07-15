using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Hardware;
using SimpleModelServer.Providers.Ollama;
using SimpleModelServer.Server.Interfaces;
using SimpleModelServer.Server.Infrastructure.Networking;
using SimpleModelServer.Server.Configuration;

using Microsoft.Extensions.Logging;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server;

/// <summary>
///     Owns the HttpListener loop and all HTTP request/response plumbing for the draft-pair
///     router: trusted-network enforcement, CORS headers, GET/POST route dispatch, health and
///     capacity endpoints, and the Ollama-native / OpenAI-compatible / embeddings request
///     handlers. Delegates actual generation to <see cref="SimpleModelPairGenerationService" />,
///     agent detection to <see cref="AgentTypeResolver" />, and health state to
///     <see cref="ModelHealthMonitor" /> — this class is "just" the transport layer wiring
///     those together.
/// </summary>
internal sealed partial class RouterHttpServer(
    IAppConfig config,
    ILogger logger,
    OllamaProcessManager process,
    ModelHealthMonitor health,
    SimpleModelPairGenerationService generation,
    AgentTypeResolver agentTypeResolver,
    HttpClient http,
    Func<ResolvedHardwareSettings> hardwareSettings,
    Func<string> currentAgentType,
    IOllamaHelpers ollamaHelpers,
    SystemInfoService systemInfoService,
    IConfigurationManager configurationManager,
    IConfigurationUpdater configurationUpdater,
    IAdminAuthenticationService adminAuthenticationService) : IRouter, IOllama
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Method}] {Path}")]
    private static partial void LogRequest(ILogger logger, string method, string path);

    private static readonly TimeSpan RouterHealthProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ModelResponsiveWarmupCheckTimeout = TimeSpan.FromSeconds(2);

    private string RouterHost => config.Global.RouterHost;
    private string RouterModelName => config.Global.RouterModelName;

    private readonly string defaultAgentType = config.Global.DefaultAgentType;

    private TrustedNetworkList TrustedNetworks
        => field ??= TrustedNetworkList.FromConfig(config.Global.TrustedNetworks);

    /// <summary>
    ///     Dumps the full request fingerprint for a request that arrived with no User-Agent, so we
    ///     can spot any other header we could use to attribute/identify the client. Logs every
    ///     request header (Authorization redacted to just its scheme), the remote endpoint, and the
    ///     protocol/keep-alive state. Info level, but only reached for unattributed requests.
    /// </summary>
    private void LogUnattributedRequestFingerprint(
        HttpListenerRequest request, string method, string path, string remoteIp)
    {
        StringBuilder headers = new();
        foreach (string? name in request.Headers.AllKeys)
        {
            if (name is null)
            {
                continue;
            }

            string value = request.Headers[name] ?? "";

            // Never log credentials — keep only the auth scheme (e.g. "Bearer", "Basic") so we can
            // still see THAT a client authenticates and how, without leaking the token itself.
            if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                int spaceIdx = value.IndexOf(' ', StringComparison.Ordinal);
                value = spaceIdx > 0 ? $"{value[..spaceIdx]} <redacted>" : "<redacted>";
            }

            headers.Append("\n    ").Append(name).Append(": ").Append(value);
        }

        logger.LogInformation(
            "Unattributed request (no User-Agent) — fingerprint:\n" +
            "  {Method} {Path}\n" +
            "  Remote: {RemoteEndPoint}  (IP {RemoteIp})\n" +
            "  Protocol: HTTP/{ProtocolVersion}  KeepAlive={KeepAlive}\n" +
            "  Headers:{Headers}",
            method, path,
            request.RemoteEndPoint, remoteIp,
            request.ProtocolVersion, request.KeepAlive,
            headers.ToString());
    }

    public async Task<bool> IsRouterAlreadyRunningAsync(int port)
    {
        try
        {
            using CancellationTokenSource cts = new(RouterHealthProbeTimeout);
            using HttpResponseMessage response =
                await http.GetAsync(new Uri($"http://{RouterHost}:{port}/health"), cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Starts the router listener. <paramref name="onBound" />, if provided, is invoked once
    ///     the port has actually been bound (after the "ROUTER SERVER STARTED" log line) — used
    ///     by <see cref="SimpleModelPairApp" /> to start mDNS advertising only once there's a real,
    ///     listening router to advertise, not when this instance detected an already-running
    ///     router and skipped its own bind (see the early-return above).
    /// </summary>
    public async Task StartAsync(int port, CancellationToken ct = default, Action? onBound = null)
    {
        if (await IsRouterAlreadyRunningAsync(port))
        {
            logger.LogWarning(
                "\nA SimpleModelServer.Server router is already listening on http://{RouterHost}:{Port} — this instance will not start a second one.",
                RouterHost, port);
            logger.LogWarning(
                "Ollama and the models were still warmed up by this process; only the router bind was skipped.");
            return;
        }

        using HttpListener listener = new();
        listener.Prefixes.Add($"http://{RouterHost}:{port}/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex) when (OllamaProcessManager.IsAddressInUse(ex))
        {
            logger.LogError(
                ex,
                "\nCould not bind router to http://{RouterHost}:{Port} — the port is already in use by another process " +
                "(not a healthy Simple Model Server router, since the health probe failed).",
                RouterHost, port);
            logger.LogWarning(
                "Free the port (check for a stuck SimpleModelServer.Server/other process) or set a different port via " +
                "--router-port / global.routerPort in SimpleModelServer.config.json.");
            return;
        }

        logger.LogInformation("\n=== ROUTER SERVER STARTED ===");
        Console.WriteLine($"Listening on http://{RouterHost}:{port}");
        Console.WriteLine(
            "Endpoints: POST /api/generate, POST /api/chat, POST /v1/chat/completions, POST /chat/completions, POST /api/embeddings, POST /v1/embeddings, POST /embeddings, GET /v1/models, GET /models, GET /api/tags, GET /api/version, GET /health, GET /health/ready, GET /api/capacity, GET /api/system/info");
        Console.WriteLine(
            "Admin Endpoints: GET /admin/config, POST /admin/config, POST /admin/reload (localhost or Bearer token required)");

        onBound?.Invoke();

        await using CancellationTokenRegistration ctReg = ct.Register(listener.Stop);

        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct = default)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        IPAddress? remoteAddress = request.RemoteEndPoint?.Address;
        bool isLoopback = remoteAddress is not null &&
                         (IPAddress.IsLoopback(remoteAddress) ||
                          remoteAddress.Equals(IPAddress.IPv6Loopback));

        if (!isLoopback && !TrustedNetworks.IsAllowed(remoteAddress))
        {
            // Not loopback and not in trusted networks — require API key
            string? apiKey = ExtractApiKey(request);
            if (!IsValidApiKey(apiKey))
            {
                logger.LogWarning(
                    "[AUTH FAILED] {RemoteAddress} is not loopback/trusted and provided invalid or missing API key",
                    remoteAddress?.ToString() ?? "unknown");
                await WriteJsonAsync(context, 401, new { error = "unauthorized: missing or invalid API key" });
                return;
            }
        }

        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Headers", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

        string path = request.Url?.AbsolutePath.ToUpperInvariant() ?? "/";
        string method = request.HttpMethod.ToUpperInvariant();
        string queryString = request.Url?.Query ?? "";
        string userAgent = request.Headers["User-Agent"] ?? "";
        string contentType = request.ContentType ?? "";

        LogRequest(logger, method, path);
        string remoteIp = request.RemoteEndPoint?.Address?.ToString() ?? "unknown-ip";
        string clientInfo;
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            clientInfo = $"{remoteIp}";
        }
        else if (userAgent.Length > 80)
        {
            clientInfo = $"{remoteIp} {userAgent[..77]}...";
        }
        else
        {
            clientInfo = $"{remoteIp} {userAgent}";
        }
        logger.LogInformation("[{Method}] {Path} from {Client}", method, path, clientInfo);

        if (!string.IsNullOrEmpty(queryString))
        {
            logger.LogDebug("Query string: {QueryString}", queryString);
        }

        logger.LogDebug("Headers - User-Agent: {UserAgent}, Content-Type: {ContentType}", userAgent, contentType);

        // When a request arrives with no User-Agent we can't attribute it the normal way. If the
        // global.debug.logUnattributedRequests diagnostic is enabled, dump the full request
        // fingerprint (every header, remote endpoint, protocol) so we can eyeball whether some OTHER
        // header carries a usable client signal (X-*, Authorization scheme, Accept quirks, a custom
        // app header) worth keying detection off of. Off by default — opt in while investigating.
        if (config.Global.Debug.LogUnattributedRequests && string.IsNullOrWhiteSpace(userAgent))
        {
            LogUnattributedRequestFingerprint(request, method, path, remoteIp);
        }

        try
        {
            if (method == "OPTIONS")
            {
                await WriteJsonAsync(context, 200, new { });
                return;
            }

            if (method == "GET")
            {
                await RouteGetAsync(context, path);
                return;
            }

            if (method == "POST")
            {
                await RoutePostAsync(context, path, ct);
                return;
            }

            logger.LogWarning("[{Method}] {Path} — 404 not found", method, path);
            await WriteJsonAsync(context, 404, new { error = "not found" });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Router is shutting down mid-request — nothing more to write, the listener
            // itself is being torn down. Don't log this as an error.
        }
        catch (HttpListenerException ex) when (ex.NativeErrorCode == 1229)
        {
            // Error 1229: "An operation was attempted on a nonexistent network connection"
            // This happens when the client closes the connection before we finish writing
            // the response. Not a server error — just a client that went away.
            logger.LogDebug("[{Method}] {Path} — client disconnected before response could be sent.", method, path);
        }
        catch (Exception ex)
        {
            // Log the full exception (message + stack trace) server-side for debugging, but
            // don't echo ex.Message back to the HTTP client — this router is normally
            // localhost-only, but exception messages can still incidentally contain file
            // paths, connection strings, or other internal details depending on where the
            // failure originated, and there's no reason for a generic 500 to leak them.
            logger.LogError(ex, "[{Method}] {Path} failed.", method, path);

            // A stack trace here means something actually broke — even if Ollama and the
            // models still separately probe as healthy, a caller polling /health should see
            // that reflected. See ModelHealthMonitor.RecordFault for why this is a time-boxed
            // signal rather than something the periodic recovery loop tries to fix.
            health.RecordFault(ex);

            await WriteJsonAsync(context, 500, new { error = "internal server error" });
        }
    }

    private async Task RouteGetAsync(HttpListenerContext context, string path)
    {
        switch (path)
        {
            case "/HEALTH":
                await HandleHealthAsync(context);
                return;
            case "/HEALTH/READY":
                await HandleHealthReadyAsync(context);
                return;
            case "/API/VERSION":
                await WriteJsonAsync(context, 200, BuildVersionBody());
                return;
            case "/API/TAGS":
                await WriteJsonAsync(context, 200, BuildTagsBody());
                return;
            case "/API/PS":
                // Same case-sensitivity note as /API/EMBED above — Ollama's own router only
                // registers the lowercase path.
                await ProxyGetToOllamaAsync(context, "/api/ps");
                return;
            case "/API/CAPACITY":
                await HandleCapacityAsync(context);
                return;
            case "/API/SYSTEM/INFO":
                await HandleSystemInfoAsync(context);
                return;
            case "/ADMIN/CONFIG":
                await HandleGetConfigAsync(context);
                return;
            case "/ADMIN":
            case "/ADMIN/":
                await ServeAdminUIAsync(context);
                return;
        }

        if (path.StartsWith("/V1/MODELS", StringComparison.Ordinal) || path.StartsWith("/MODELS", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context, 200, BuildModelsListBody());
            return;
        }

        logger.LogWarning("[GET] {Path} — 404 not found", path);
        await WriteJsonAsync(context, 404, new { error = "not found" });
    }

    // ── Response builders shared between the real route dispatch above and the /health
    //    self-test suite below, so the self-test exercises the exact same body-construction
    //    code a real client's request would run — not a separate reimplementation of it. ──

    private static object BuildVersionBody()
    {
        return new { version = "0.1.0" };
    }

    private object BuildTagsBody()
    {
        return new
        {
            models = new[]
                { new { name = RouterModelName, modified_at = DateTimeOffset.UtcNow.ToString("O"), size = 0 } }
        };
    }

    private object BuildModelsListBody()
    {
        return new
        {
            @object = "list",
            data = new[] { new { id = RouterModelName, @object = "model", created = 0, owned_by = "local" } }
        };
    }

    private async Task RoutePostAsync(HttpListenerContext context, string path, CancellationToken ct = default)
    {
        switch (path)
        {
            case "/API/GENERATE":
                await HandleOllamaGenerateAsync(context, ct);
                return;
            case "/API/CHAT":
            {
                // Detect client type and respond with appropriate format
                ClientDetector.ClientType clientType = ClientDetector.Detect(context.Request);
                if (clientType == ClientDetector.ClientType.Ollama)
                {
                    await HandleOllamaChatAsync(context, ct);
                }
                else
                {
                    await HandleOpenAiChatAsync(context, ct);
                }
                return;
            }
            case "/V1/CHAT/COMPLETIONS":
            case "/CHAT/COMPLETIONS":
                await HandleOpenAiChatAsync(context, ct);
                return;
            case "/V1/COMPLETIONS":
            case "/COMPLETIONS":
                await HandleOpenAiCompletionAsync(context, ct);
                return;
            case "/API/EMBED":
                // ollamaPath is forwarded verbatim to Ollama's own (case-sensitive) HTTP router,
                // so it must be the real lowercase route, not the uppercased string used for
                // switch-matching just above.
                await ProxyToOllamaAsync(context, "/api/embed", ct);
                return;
            case "/API/EMBEDDINGS":
            case "/V1/EMBEDDINGS":
            case "/EMBEDDINGS":
                await ProxyEmbeddingsAsync(context, ct);
                return;
            case "/ADMIN/CONFIG":
                await HandleUpdateConfigAsync(context, ct);
                return;
            case "/ADMIN/RELOAD":
                await HandleReloadAsync(context, ct);
                return;
            default:
                string body = await ReadBodyAsync(context.Request);
                string bodyPreview = body.Length > 500 ? body[..500] + "..." : body;
                logger.LogWarning("[POST] {Path} — 404 not found. Body: {Body}", path, bodyPreview);
                await WriteJsonAsync(context, 404, new { error = "not found" });
                return;
        }
    }

    /// <summary>
    ///     Checks if models are warming up and returns a 202 response if they are.
    ///     Returns true if any model is warming up (caller should not proceed with request).
    /// </summary>
    private async Task<bool> CheckModelWarmupAsync(HttpListenerContext context)
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

        logger.LogWarning("Health check failed — attempting recovery...");
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

    private async Task HandleHealthAsync(HttpListenerContext context)
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

    private async Task HandleHealthReadyAsync(HttpListenerContext context)
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
    private async Task HandleCapacityAsync(HttpListenerContext context)
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
    private async Task HandleSystemInfoAsync(HttpListenerContext context)
    {
        var systemInfo = systemInfoService.GetSystemInfo();
        await WriteJsonAsync(context, 200, systemInfo);
    }

    private async Task HandleOllamaGenerateAsync(HttpListenerContext context, CancellationToken ct = default)
    {
        if (await CheckModelWarmupAsync(context))
        {
            return;
        }

        string body = await ReadBodyAsync(context.Request);
        JsonNode? json = JsonNode.Parse(body);
        string prompt = json?["prompt"]?.GetValue<string>() ?? "";


        // Unknown agent types must fall back to the configured default agent type,
        // not the "current agent" (which caused 202 Accepted responses in tests).
        string fallbackAgentType = config.DefaultAgentType;
        string agentType = agentTypeResolver.Resolve(context.Request, json, prompt, fallbackAgentType);


        bool stream = json?["stream"]?.GetValue<bool>() ?? true;

        // ── Research path ──
        if (string.Equals(agentType, "research", StringComparison.OrdinalIgnoreCase))
        {
            bool streamedDirectly = false;

            string responseText = await generation.ResolveResearchResponseAsync(prompt, async result =>
            {
                // Ollama-native streaming has a fast path the OpenAI-compatible handler doesn't
                // use: when web results are found, stream the draft-pair response to the
                // enriched prompt directly instead of waiting for the full text.
                if (!stream)
                {
                    return false;
                }

                string enriched = SimpleModelPairGenerationService.BuildResearchPromptFor(result);
                context.Response.ContentType = "application/x-ndjson";
                context.Response.StatusCode = 200;
                await generation.StreamDraftPairAsync(enriched, "research", async token =>
                    await WriteRawAsync(context.Response,
                        JsonSerializer.Serialize(new { response = token, done = false }) + "\n"), ct);
                await WriteRawAsync(context.Response,
                    JsonSerializer.Serialize(new { response = "", done = true }) + "\n");
                context.Response.Close();
                streamedDirectly = true;
                return true;
            }, ct);

            if (streamedDirectly)
            {
                return;
            }

            if (stream)
            {
                context.Response.ContentType = "application/x-ndjson";
                context.Response.StatusCode = 200;
                await WriteRawAsync(context.Response,
                    JsonSerializer.Serialize(new { response = responseText, done = false }) + "\n");
                await WriteRawAsync(context.Response,
                    JsonSerializer.Serialize(new { response = "", done = true }) + "\n");
                context.Response.Close();
            }
            else
            {
                await WriteJsonAsync(context, 200,
                    new { model = RouterModelName, response = responseText, done = true });
            }

            return;
        }

        if (!stream)
        {
            string final = await generation.GetDraftPairResponseAsync([new ChatMessage("user", prompt)], agentType, ct);
            await WriteJsonAsync(context, 200, new { model = RouterModelName, response = final, done = true });
            return;
        }

        context.Response.ContentType = "application/x-ndjson";
        context.Response.StatusCode = 200;

        await generation.StreamDraftPairAsync(prompt, agentType,
            async token =>
            {
                await WriteRawAsync(context.Response,
                    JsonSerializer.Serialize(new { response = token, done = false }) + "\n");
            }, ct);

        await WriteRawAsync(context.Response, JsonSerializer.Serialize(new { response = "", done = true }) + "\n");
        context.Response.Close();
    }

    private async Task HandleOpenAiChatAsync(HttpListenerContext context, CancellationToken ct = default)
    {
        if (await CheckModelWarmupAsync(context))
        {
            return;
        }

        string body = await ReadBodyAsync(context.Request);
        JsonNode? json = JsonNode.Parse(body);
        JsonArray messages = json?["messages"]?.AsArray() ?? [];
        bool stream = json?["stream"]?.GetValue<bool>() ?? false;
        string prompt = ollamaHelpers.BuildPrompt(messages);
        string agentType = agentTypeResolver.Resolve(context.Request, json, prompt, currentAgentType());

        // ── Research path ──
        if (string.Equals(agentType, "research", StringComparison.OrdinalIgnoreCase))
        {
            // Unlike the Ollama-native handler, OpenAI-compatible streaming here always waits
            // for the full response text (SSE emits it as a single delta chunk below) rather
            // than streaming the draft-pair tokens live, so no onWebResultsFound hook is needed.
            string responseText = await generation.ResolveResearchResponseAsync(prompt, ct: ct);

            if (stream)
            {
                context.Response.ContentType = "text/event-stream";
                context.Response.StatusCode = 200;
                string payload = JsonSerializer.Serialize(new
                { choices = new[] { new { delta = new { content = responseText } } } });
                await WriteRawAsync(context.Response, $"data: {payload}\n\n");
                await WriteRawAsync(context.Response, "data: [DONE]\n\n");
                context.Response.Close();
            }
            else
            {
                await WriteJsonAsync(context, 200, new
                {
                    id = $"chatcmpl-{Guid.NewGuid()}",
                    @object = "chat.completion",
                    choices = new[]
                    {
                        new
                        {
                            index = 0, message = new { role = "assistant", content = responseText },
                            finish_reason = "stop"
                        }
                    }
                });
            }

            return;
        }

        if (stream)
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.StatusCode = 200;
            await generation.StreamDraftPairAsync(prompt, agentType, async token =>
            {
                string payload = JsonSerializer.Serialize(new
                { choices = new[] { new { delta = new { content = token } } } });
                await WriteRawAsync(context.Response, $"data: {payload}\n\n");
            }, ct);
            await WriteRawAsync(context.Response, "data: [DONE]\n\n");
            context.Response.Close();
            return;
        }

        string final2 = await generation.GetDraftPairResponseAsync([new ChatMessage("user", prompt)], agentType, ct);
        await WriteJsonAsync(context, 200, new
        {
            id = $"chatcmpl-{Guid.NewGuid()}",
            @object = "chat.completion",
            choices = new[]
            {
                new { index = 0, message = new { role = "assistant", content = final2 }, finish_reason = "stop" }
            }
        });
    }

    private async Task HandleOllamaChatAsync(HttpListenerContext context, CancellationToken ct = default)
    {
        if (await CheckModelWarmupAsync(context))
        {
            return;
        }

        string body = await ReadBodyAsync(context.Request);
        JsonNode? json = JsonNode.Parse(body);
        JsonArray messages = json?["messages"]?.AsArray() ?? [];
        bool stream = json?["stream"]?.GetValue<bool>() ?? false;
        string prompt = ollamaHelpers.BuildPrompt(messages);
        string agentType = agentTypeResolver.Resolve(context.Request, json, prompt, currentAgentType());

        // ── Research path ──
        if (string.Equals(agentType, "research", StringComparison.OrdinalIgnoreCase))
        {
            string responseText = await generation.ResolveResearchResponseAsync(prompt, ct: ct);

            if (stream)
            {
                context.Response.ContentType = "application/x-ndjson";
                context.Response.StatusCode = 200;
                await WriteRawAsync(context.Response,
                    JsonSerializer.Serialize(new { message = new { role = "assistant", content = responseText }, done = false }) + "\n");
                await WriteRawAsync(context.Response,
                    JsonSerializer.Serialize(new { message = new { role = "assistant", content = "" }, done = true }) + "\n");
                context.Response.Close();
            }
            else
            {
                await WriteJsonAsync(context, 200, new
                {
                    model = RouterModelName,
                    message = new { role = "assistant", content = responseText },
                    done = true
                });
            }

            return;
        }

        if (stream)
        {
            context.Response.ContentType = "application/x-ndjson";
            context.Response.StatusCode = 200;
            await generation.StreamDraftPairAsync(prompt, agentType, async token =>
                await WriteRawAsync(context.Response,
                    JsonSerializer.Serialize(new { message = new { role = "assistant", content = token }, done = false }) + "\n"), ct);
            await WriteRawAsync(context.Response,
                JsonSerializer.Serialize(new { message = new { role = "assistant", content = "" }, done = true }) + "\n");
            context.Response.Close();
            return;
        }

        string final = await generation.GetDraftPairResponseAsync([new ChatMessage("user", prompt)], agentType, ct);
        await WriteJsonAsync(context, 200, new
        {
            model = RouterModelName,
            message = new { role = "assistant", content = final },
            done = true
        });
    }

    private async Task HandleOpenAiCompletionAsync(HttpListenerContext context, CancellationToken ct = default)
    {
        if (await CheckModelWarmupAsync(context))
        {
            return;
        }

        string body = await ReadBodyAsync(context.Request);
        JsonNode? json = JsonNode.Parse(body);
        string prompt = json?["prompt"]?.GetValue<string>() ?? "";


        string fallbackAgentType = defaultAgentType;
        string agentType = agentTypeResolver.Resolve(context.Request, json, prompt, fallbackAgentType);


        string final = await generation.GetDraftPairResponseAsync([new ChatMessage("user", prompt)], agentType, ct);
        await WriteJsonAsync(context, 200, new
        {
            id = $"cmpl-{Guid.NewGuid()}",
            @object = "text_completion",
            choices = new[] { new { index = 0, text = final, finish_reason = "stop" } }
        });
    }

    private async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        string body;
        using (StreamReader reader = new(request.InputStream, request.ContentEncoding))
        {
            body = await reader.ReadToEndAsync();
        }

        // global.debug.logRequestBodies: log the (truncated) request body as it's read. This is the
        // single choke point every POST handler funnels through, so logging here catches every body
        // exactly once without re-reading the (once-only) input stream or threading the text through
        // handler signatures. Verbose and potentially sensitive (bodies carry prompt text) — off by
        // default; intended for short local debugging sessions only.
        if (config.Global.Debug.LogRequestBodies)
        {
            const int MaxBodyLog = 2000;
            string preview = body.Length > MaxBodyLog
                ? $"{body[..MaxBodyLog]}… (+{body.Length - MaxBodyLog} more chars)"
                : body;
            logger.LogInformation(
                "[{Method}] {Path} body ({Length} chars):\n{Body}",
                request.HttpMethod.ToUpperInvariant(),
                request.Url?.AbsolutePath ?? "/",
                body.Length,
                preview);
        }

        return body;
    }

    /// <summary>
    ///     Pass the request body straight through to Ollama and stream the response back.
    ///     Used for /api/embed and any other endpoints that don't need draft-pair logic.
    /// </summary>
    private async Task ProxyToOllamaAsync(HttpListenerContext context, string ollamaPath, CancellationToken ct = default)
    {
        string body = await ReadBodyAsync(context.Request);
        using StringContent content = new(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage ollamaResponse = await http.PostAsync(new Uri($"http://127.0.0.1:{process.ServerPort}{ollamaPath}"), content, ct);

        context.Response.StatusCode = (int)ollamaResponse.StatusCode;
        context.Response.ContentType = ollamaResponse.Content.Headers.ContentType?.ToString() ?? "application/json";

        byte[] responseBody = await ollamaResponse.Content.ReadAsByteArrayAsync(ct);
        context.Response.ContentLength64 = responseBody.Length;
        await context.Response.OutputStream.WriteAsync(responseBody, ct);
        context.Response.Close();
    }

    private async Task ProxyGetToOllamaAsync(HttpListenerContext context, string ollamaPath)
    {
        using HttpResponseMessage ollamaResponse = await http.GetAsync(new Uri($"http://127.0.0.1:{process.ServerPort}{ollamaPath}"));
        context.Response.StatusCode = (int)ollamaResponse.StatusCode;
        context.Response.ContentType = ollamaResponse.Content.Headers.ContentType?.ToString() ?? "application/json";
        byte[] responseBody = await ollamaResponse.Content.ReadAsByteArrayAsync();
        context.Response.ContentLength64 = responseBody.Length;
        await context.Response.OutputStream.WriteAsync(responseBody);
        context.Response.Close();
    }

    /// <summary>
    ///     Handle OpenAI-compatible /v1/embeddings by translating to Ollama /api/embed and back.
    /// </summary>
    private async Task ProxyEmbeddingsAsync(HttpListenerContext context, CancellationToken ct = default)
    {
        string body = await ReadBodyAsync(context.Request);
        JsonNode? json = JsonNode.Parse(body);
        JsonNode? input = json?["input"];
        string model = json?["model"]?.GetValue<string>() ?? generation.EmbedModelName;

        // Translate OpenAI → Ollama format
        JsonObject ollamaPayload = new() { ["model"] = model, ["input"] = input?.DeepClone() };
        using StringContent content = new(ollamaPayload.ToJsonString(), Encoding.UTF8, "application/json");

        using HttpResponseMessage ollamaResponse = await http.PostAsync(new Uri($"http://127.0.0.1:{process.ServerPort}/api/embed"), content, ct);
        if (!ollamaResponse.IsSuccessStatusCode)
        {
            await WriteJsonAsync(context, (int)ollamaResponse.StatusCode,
                new { error = $"Embed model unavailable ({(int)ollamaResponse.StatusCode})" });
            return;
        }

        JsonNode? ollamaJson = JsonNode.Parse(await ollamaResponse.Content.ReadAsStringAsync(ct));
        JsonArray? embeddings = ollamaJson?["embeddings"]?.AsArray();

        JsonArray dataArray = [];
        if (embeddings is not null)
        {
            for (int i = 0; i < embeddings.Count; i++)
            {
                dataArray.Add(new JsonObject
                {
                    ["object"] = "embedding",
                    ["embedding"] = embeddings[i]?.DeepClone(),
                    ["index"] = i
                });
            }
        }

        await WriteJsonAsync(context, 200, new
        {
            @object = "list",
            data = dataArray,
            model
        });
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object body)
    {
        string json = JsonSerializer.Serialize(body);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static async Task WriteRawAsync(HttpListenerResponse response, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await response.OutputStream.WriteAsync(bytes);
        await response.OutputStream.FlushAsync();
    }

    // ── IRouter explicit implementations ──

    Task IRouter.HandleHealthAsync(HttpListenerContext context) => HandleHealthAsync(context);

    Task IRouter.HandleHealthReadyAsync(HttpListenerContext context) => HandleHealthReadyAsync(context);

    Task IRouter.HandleCapacityAsync(HttpListenerContext context) => HandleCapacityAsync(context);

    // ── IOllama explicit implementations ──

    Task IOllama.HandleGetApiTagsAsync(HttpListenerContext context)
    {
        // /api/tags is handled in RouteGetAsync, delegating to BuildTagsBody
        return HandleGetApiTagsAsync(context);
    }

    private async Task HandleGetApiTagsAsync(HttpListenerContext context)
    {
        await WriteJsonAsync(context, 200, BuildTagsBody());
    }

    Task IOllama.HandleGetApiVersionAsync(HttpListenerContext context)
    {
        return HandleGetApiVersionAsync(context);
    }

    private async Task HandleGetApiVersionAsync(HttpListenerContext context)
    {
        await WriteJsonAsync(context, 200, BuildVersionBody());
    }

    Task IOllama.HandleGetApiPsAsync(HttpListenerContext context)
    {
        // Proxy to Ollama's /api/ps
        return ProxyGetToOllamaAsync(context, "/api/ps");
    }

    Task IOllama.HandlePostApiGenerateAsync(HttpListenerContext context, CancellationToken ct)
        => HandleOllamaGenerateAsync(context, ct);

    Task IOllama.HandlePostApiChatAsync(HttpListenerContext context, CancellationToken ct)
    {
        // /api/chat is handled via OpenAI-compatible handler
        return HandleOpenAiChatAsync(context, ct);
    }

    Task IOllama.HandlePostApiEmbedAsync(HttpListenerContext context, CancellationToken ct)
    {
        // Proxy to Ollama's /api/embed
        return ProxyToOllamaAsync(context, "/api/embed", ct);
    }

    Task IOllama.HandlePostApiEmbeddingsAsync(HttpListenerContext context, CancellationToken ct)
    {
        // Legacy endpoint — map to /v1/embeddings handler
        return ProxyEmbeddingsAsync(context, ct);
    }

    Task IOllama.HandlePostApiShowAsync(HttpListenerContext context, CancellationToken ct)
    {
        // Proxy to Ollama's /api/show
        return ProxyToOllamaAsync(context, "/api/show", ct);
    }

    Task IOllama.HandlePostApiCreateAsync(HttpListenerContext context, CancellationToken ct)
    {
        // Proxy to Ollama's /api/create
        return ProxyToOllamaAsync(context, "/api/create", ct);
    }

    Task IOllama.HandlePostApiCopyAsync(HttpListenerContext context, CancellationToken ct)
    {
        // Proxy to Ollama's /api/copy
        return ProxyToOllamaAsync(context, "/api/copy", ct);
    }

    Task IOllama.HandleDeleteApiAsync(HttpListenerContext context, CancellationToken ct)
    {
        // Proxy DELETE /api/delete to Ollama
        return ProxyDeleteToOllamaAsync(context, "/api/delete", ct);
    }

    Task IOllama.HandlePostApiPullAsync(HttpListenerContext context, CancellationToken ct)
    {
        // Proxy to Ollama's /api/pull
        return ProxyToOllamaAsync(context, "/api/pull", ct);
    }

    Task IOllama.HandlePostApiPushAsync(HttpListenerContext context, CancellationToken ct)
    {
        // Proxy to Ollama's /api/push
        return ProxyToOllamaAsync(context, "/api/push", ct);
    }

    Task IOllama.HandleHeadApiBlobsAsync(HttpListenerContext context, string digest)
    {
        // Proxy HEAD /api/blobs/:digest to Ollama
        return ProxyHeadToOllamaAsync(context, $"/api/blobs/{Uri.EscapeDataString(digest)}");
    }

    Task IOllama.HandlePostApiBlobsAsync(HttpListenerContext context, string digest, CancellationToken ct)
    {
        // Proxy POST /api/blobs/:digest to Ollama
        return ProxyToOllamaAsync(context, $"/api/blobs/{Uri.EscapeDataString(digest)}", ct);
    }

    // ── Proxy helpers for new Ollama endpoints ──

    private async Task ProxyDeleteToOllamaAsync(HttpListenerContext context, string ollamaPath, CancellationToken ct = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, new Uri($"http://127.0.0.1:{process.ServerPort}{ollamaPath}"));
        using HttpResponseMessage ollamaResponse = await http.SendAsync(request, ct);
        context.Response.StatusCode = (int)ollamaResponse.StatusCode;
        context.Response.ContentType = ollamaResponse.Content.Headers.ContentType?.ToString() ?? "application/json";
        byte[] responseBody = await ollamaResponse.Content.ReadAsByteArrayAsync(ct);
        context.Response.ContentLength64 = responseBody.Length;
        await context.Response.OutputStream.WriteAsync(responseBody, ct);
        context.Response.Close();
    }

    private async Task ProxyHeadToOllamaAsync(HttpListenerContext context, string ollamaPath)
    {
        using HttpRequestMessage request = new(HttpMethod.Head, new Uri($"http://127.0.0.1:{process.ServerPort}{ollamaPath}"));
        using HttpResponseMessage ollamaResponse = await http.SendAsync(request);
        context.Response.StatusCode = (int)ollamaResponse.StatusCode;
        context.Response.Close();
    }

    private string? ExtractApiKey(HttpListenerRequest request)
    {
        string? auth = request.Headers["Authorization"];
        if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth["Bearer ".Length..].Trim();
        }

        return request.Headers["X-API-Key"];
    }

    private bool IsValidApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        return config.Global.TrustedApiKeys.Contains(apiKey, StringComparer.Ordinal);
    }

    // ── Configuration Admin Endpoints ──

    /// <summary>
    ///     Serves the admin UI HTML page.
    /// </summary>
    private async Task ServeAdminUIAsync(HttpListenerContext context)
    {
        try
        {
            string adminUiPath = Path.Combine(AppContext.BaseDirectory, "admin", "index.html");
            if (!File.Exists(adminUiPath))
            {
                logger.LogWarning("[ADMIN] Admin UI not found at {Path}", adminUiPath);
                await WriteJsonAsync(context, 404, new { error = "Admin UI not available" });
                return;
            }

            string html = await File.ReadAllTextAsync(adminUiPath);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.StatusCode = 200;

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ADMIN] Error serving admin UI");
            await WriteJsonAsync(context, 500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    ///     GET /admin/config — Retrieves the current configuration as JSON.
    ///     Requires admin authentication (localhost or valid API key).
    /// </summary>
    private async Task HandleGetConfigAsync(HttpListenerContext context)
    {
        if (!adminAuthenticationService.IsAuthorized(context.Request))
        {
            logger.LogWarning("[ADMIN] [GET] /admin/config — unauthorized access attempt from {RemoteAddress}",
                context.Request.RemoteEndPoint?.Address);
            await WriteJsonAsync(context, 401, new
            {
                error = "unauthorized",
                message = "Admin configuration requires authentication (localhost trusted by default, remote requests require Bearer token)"
            });
            return;
        }

        logger.LogInformation("[ADMIN] [GET] /admin/config from {RemoteAddress}", context.Request.RemoteEndPoint?.Address);

        var configData = new
        {
            global = new
            {
                routerPort = config.Global.RouterPort,
                ollamaPort = config.Global.OllamaPort,
                routerHost = config.Global.RouterHost,
                routerModelName = config.Global.RouterModelName,
                draftModel = config.Global.DraftModel,
                mainModel = config.Global.MainModel,
                embedModel = config.Global.EmbedModel,
                contextSize = config.Global.ContextSize,
                defaultAgentType = config.Global.DefaultAgentType,
                agentType = config.Global.AgentType,
                draftHandoffTimeoutMs = config.Global.DraftHandoffTimeoutMs,
                draftMode = config.Global.DraftMode.ToString(),
                trustedNetworks = config.Global.TrustedNetworks,
                trustedApiKeys = new string[0], // Never return actual keys for security
                mdnsEnabled = config.Global.MdnsEnabled,
                mdnsServiceName = config.Global.MdnsServiceName,
                gpuLayers = config.Global.GpuLayers.IsSpecified ?
                    (config.Global.GpuLayers.Auto ? "auto" : config.Global.GpuLayers.Value?.ToString() ?? "unspecified")
                    : "unspecified",
                cpuThreads = config.Global.CpuThreads.IsSpecified ?
                    (config.Global.CpuThreads.Auto ? "auto" : config.Global.CpuThreads.Value?.ToString() ?? "unspecified")
                    : "unspecified"
            },
            agents = config.Agents.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    systemPrompt = kvp.Value.SystemPrompt,
                    temperature = kvp.Value.Temperature
                }),
            modelCount = config.ModelSources.Count,
            note = "Localhost is always trusted. Remote requests require 'Authorization: Bearer <key>' header."
        };

        await WriteJsonAsync(context, 200, configData);
    }

    /// <summary>
    ///     POST /admin/config — Updates a configuration setting.
    ///     Body: { "setting": "global.routerPort", "value": 11436 }
    ///     Returns: { "success": true, "requiresRestart": true, "message": "..." }
    /// </summary>
    private async Task HandleUpdateConfigAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (!adminAuthenticationService.IsAuthorized(context.Request))
        {
            logger.LogWarning("[ADMIN] [POST] /admin/config — unauthorized access attempt from {RemoteAddress}",
                context.Request.RemoteEndPoint?.Address);
            await WriteJsonAsync(context, 401, new
            {
                error = "unauthorized",
                message = "Admin configuration requires authentication (localhost trusted by default, remote requests require Bearer token)"
            });
            return;
        }

        try
        {
            string bodyText = await ReadBodyAsync(context.Request);
            JsonNode? requestBody = JsonNode.Parse(bodyText);
            if (requestBody is not JsonObject bodyObj)
            {
                await WriteJsonAsync(context, 400, new { error = "Invalid request body. Expected JSON object." });
                return;
            }

            string? settingPath = bodyObj["setting"]?.GetValue<string>();
            object? value = bodyObj["value"];

            if (string.IsNullOrWhiteSpace(settingPath))
            {
                await WriteJsonAsync(context, 400, new { error = "Missing or empty 'setting' field" });
                return;
            }

            // Validate the setting
            ValidationResult validation = configurationManager.Validate(settingPath, value);
            if (!validation.IsValid)
            {
                logger.LogWarning("[ADMIN] Invalid config update from {RemoteAddress}: {Setting} = {Value} — {Error}",
                    context.Request.RemoteEndPoint?.Address, settingPath, value, validation.ErrorMessage);
                await WriteJsonAsync(context, 400, new
                {
                    error = "validation_failed",
                    message = validation.ErrorMessage
                });
                return;
            }

            // Apply the update
            string settingName = settingPath.Contains('.', StringComparison.Ordinal) ? settingPath.Split('.')[^1] : settingPath;
            UpdateResult updateResult = configurationUpdater.UpdateGlobalSetting(config.Global, settingName, value);

            if (!updateResult.Success)
            {
                logger.LogWarning("[ADMIN] Failed to update config from {RemoteAddress}: {Setting} = {Value} — {Error}",
                    context.Request.RemoteEndPoint?.Address, settingPath, value, updateResult.ErrorMessage);
                await WriteJsonAsync(context, 400, new
                {
                    error = "update_failed",
                    message = updateResult.ErrorMessage
                });
                return;
            }

            logger.LogInformation("[ADMIN] Configuration updated from {RemoteAddress}: {Setting} = {Value} (requiresRestart={RequiresRestart})",
                context.Request.RemoteEndPoint?.Address, settingPath, value, updateResult.RequiresRestart);

            await WriteJsonAsync(context, 200, new
            {
                success = true,
                setting = settingPath,
                requiresRestart = updateResult.RequiresRestart,
                message = updateResult.RequiresRestart
                    ? $"Setting '{settingPath}' updated. Server restart required for change to take effect."
                    : $"Setting '{settingPath}' updated and reloaded."
            });
        }
        catch (JsonException ex)
        {
            logger.LogWarning("[ADMIN] Invalid JSON in config update from {RemoteAddress}: {Error}",
                context.Request.RemoteEndPoint?.Address, ex.Message);
            await WriteJsonAsync(context, 400, new
            {
                error = "invalid_json",
                message = "Request body must be valid JSON"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ADMIN] Unexpected error in config update from {RemoteAddress}",
                context.Request.RemoteEndPoint?.Address);
            await WriteJsonAsync(context, 500, new { error = "internal_server_error" });
        }
    }

    /// <summary>
    ///     POST /admin/reload — Reloads configuration changes that don't require restart.
    ///     For settings requiring restart, this does nothing (restart required via other means).
    /// </summary>
    private async Task HandleReloadAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (!adminAuthenticationService.IsAuthorized(context.Request))
        {
            logger.LogWarning("[ADMIN] [POST] /admin/reload — unauthorized access attempt from {RemoteAddress}",
                context.Request.RemoteEndPoint?.Address);
            await WriteJsonAsync(context, 401, new
            {
                error = "unauthorized",
                message = "Admin reload requires authentication"
            });
            return;
        }

        logger.LogInformation("[ADMIN] [POST] /admin/reload from {RemoteAddress}", context.Request.RemoteEndPoint?.Address);

        // Reload settings that can be hot-reloaded (trusted networks, API keys, etc.)
        // These are already applied to config.Global, so no extra work needed here.
        // In a more complex scenario, you might trigger model reloading, cache clearing, etc.

        await WriteJsonAsync(context, 200, new
        {
            success = true,
            message = "Hot-reloadable configuration reloaded. Settings requiring restart must be applied via server restart.",
            note = "For settings marked requiresRestart, restart the server to apply changes."
        });
    }
}


