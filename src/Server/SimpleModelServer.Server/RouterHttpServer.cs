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

/// <summary>
///     Owns the Kestrel host and all HTTP request/response plumbing for the draft-pair
///     router: trusted-network enforcement, CORS headers, GET/POST route dispatch, health and
///     capacity endpoints, and the Ollama-native / OpenAI-compatible / embeddings request
///     handlers. Delegates actual generation to <see cref="SimpleModelPairGenerationService" />,
///     agent detection to <see cref="AgentTypeResolver" />, and health state to
///     <see cref="ModelHealthMonitor" /> — this class is "just" the transport layer wiring
///     those together. The full request pipeline runs as a single terminal middleware (see
///     <see cref="StartAsync" />) rather than a minimal-API route table, to keep the existing
///     switch-based dispatch and its test coverage unchanged.
/// </summary>
internal sealed partial class RouterHttpServer(
	IAppConfig config,
	ILogger logger,
	IOllamaProcessManager process,
	ModelHealthMonitor health,
	SimpleModelPairGenerationService generation,
	AgentTypeResolver agentTypeResolver,
	HttpClient http,
	Func<ResolvedHardwareSettings> hardwareSettings,
	Func<string> currentAgentType,
	IOllamaHelpers ollamaHelpers,
	SystemInfoService systemInfoService,
	AdminEndpointHandler admin,
	ModelReloadCoordinator reloadCoordinator,
	McpUpstreamRegistry mcpUpstreamRegistry) : IRouter
{
	[LoggerMessage(Level = LogLevel.Debug, Message = "[{Method}] {Path}")]
	private static partial void LogRequest(ILogger logger, string method, string path);

	[LoggerMessage(Level = LogLevel.Information, Message = "Unattributed request (no User-Agent) — fingerprint:\n  {Method} {Path}\n  Remote: {RemoteEndPoint}  (IP {RemoteIp})\n  Protocol: {ProtocolVersion}  KeepAlive={KeepAlive}\n  Headers:{Headers}")]
	private static partial void LogUnattributedRequestFingerprint(ILogger logger, string method, string path, string remoteEndPoint, string remoteIp, string protocolVersion, bool keepAlive, string headers);

	[LoggerMessage(Level = LogLevel.Warning, Message = "\nA SimpleModelServer.Server router is already listening on http://{RouterHost}:{Port} — this instance will not start a second one.")]
	private static partial void LogRouterAlreadyRunning(ILogger logger, string routerHost, int port);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Ollama and the models were still warmed up by this process; only the router bind was skipped.")]
	private static partial void LogRouterSkipped(ILogger logger);

	[LoggerMessage(Level = LogLevel.Error, Message = "\nCould not bind router to http://{RouterHost}:{Port} — the port is already in use by another process (not a healthy Simple Model Server router, since the health probe failed).")]
	private static partial void LogRouterBindFailed(ILogger logger, Exception ex, string routerHost, int port);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Free the port (check for a stuck SimpleModelServer.Server/other process) or set a different port via --router-port / global.routerPort in SimpleModelServer.config.json.")]
	private static partial void LogRouterPortInUse(ILogger logger);

	[LoggerMessage(Level = LogLevel.Information, Message = "\n=== ROUTER SERVER STARTED ===")]
	private static partial void LogRouterServerStarted(ILogger logger);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[AUTH FAILED] {RemoteAddress} is not loopback/trusted and provided invalid or missing API key")]
	private static partial void LogAuthFailed(ILogger logger, string remoteAddress);

	[LoggerMessage(Level = LogLevel.Information, Message = "[{Method}] {Path} from {Client}")]
	private static partial void LogRequestClient(ILogger logger, string method, string path, string client);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Query string: {QueryString}")]
	private static partial void LogQueryString(ILogger logger, string queryString);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Headers - User-Agent: {UserAgent}, Content-Type: {ContentType}")]
	private static partial void LogHeaders(ILogger logger, string userAgent, string contentType);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[{Method}] {Path} — 404 not found")]
	private static partial void LogNotFound(ILogger logger, string method, string path);

	[LoggerMessage(Level = LogLevel.Debug, Message = "[{Method}] {Path} — client disconnected before response could be sent.")]
	private static partial void LogClientDisconnected(ILogger logger, string method, string path);

	[LoggerMessage(Level = LogLevel.Error, Message = "[{Method}] {Path} failed.")]
	private static partial void LogRequestFailed(ILogger logger, Exception ex, string method, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[GET] {Path} — 404 not found")]
	private static partial void LogGetNotFound(ILogger logger, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[POST] {Path} — 404 not found. Body: {Body}")]
	private static partial void LogPostNotFound(ILogger logger, string path, string body);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Health check failed — attempting recovery...")]
	private static partial void LogHealthCheckFailed(ILogger logger);

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
	private void LogUnattributedRequestFingerprintImpl(
		HttpContext context, string method, string path, string remoteIp)
	{
		HttpRequest request = context.Request;
		StringBuilder headers = new();
		foreach (string name in request.Headers.Keys)
		{
			string value = request.Headers[name].ToString();

			// Never log credentials — keep only the auth scheme (e.g. "Bearer", "Basic") so we can
			// still see THAT a client authenticates and how, without leaking the token itself.
			if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
			{
				int spaceIdx = value.IndexOf(' ', StringComparison.Ordinal);
				value = spaceIdx > 0 ? $"{value[..spaceIdx]} <redacted>" : "<redacted>";
			}

			headers.Append("\n    ").Append(name).Append(": ").Append(value);
		}

		string remoteEndPoint = context.Connection.RemoteIpAddress is { } addr
			? $"{addr}:{context.Connection.RemotePort}"
			: "unknown";
		bool keepAlive = !string.Equals(request.Headers.Connection.ToString(), "close", StringComparison.OrdinalIgnoreCase);
		LogUnattributedRequestFingerprint(logger, method, path, remoteEndPoint, remoteIp, request.Protocol, keepAlive, headers.ToString());
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
	/// <summary>
	///     True when the given exception is Kestrel failing to bind because the port is already
	///     held by another process — the Kestrel/ASP.NET Core equivalent of the old
	///     <see cref="OllamaProcessManager.IsAddressInUse(HttpListenerException)" /> check, which
	///     only understood <see cref="HttpListenerException" /> error codes and doesn't apply to
	///     Kestrel's bind failures (an <see cref="IOException" /> wrapping a <see cref="SocketException" />).
	/// </summary>
	private static bool IsKestrelAddressInUse(Exception ex)
		=> ex is IOException { InnerException: SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } };

	public async Task StartAsync(int port, CancellationToken ct = default, Action? onBound = null)
	{
		if (await IsRouterAlreadyRunningAsync(port))
		{
			LogRouterAlreadyRunning(logger, RouterHost, port);
			LogRouterSkipped(logger);
			return;
		}

		WebApplicationBuilder builder = WebApplication.CreateBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls($"http://{RouterHost}:{port}");
		builder.Services.AddSignalR();
		builder.Services.AddSingleton(reloadCoordinator);
		// IAppConfig/IMcpUpstreamRegistry are resolved per call for McpAdminTools (WithTools<T>()
		// constructs a fresh instance per tool invocation) — the SDK combines these
		// reflection-registered tools with the WithListToolsHandler/WithCallToolHandler pair
		// below rather than one replacing the other, so McpAdminTools's list/add/remove tools and
		// the proxied upstream tools both show up in the same gateway's tool list.
		builder.Services.AddSingleton(config);
		builder.Services.AddSingleton<IMcpUpstreamRegistry>(mcpUpstreamRegistry);
		builder.Services.AddMcpServer()
			.WithHttpTransport()
			.WithTools<McpAdminTools>()
			.WithListToolsHandler(mcpUpstreamRegistry.ListToolsAsync)
			.WithCallToolHandler(mcpUpstreamRegistry.CallToolAsync);

		await using WebApplication app = builder.Build();
		reloadCoordinator.Hub = app.Services.GetRequiredService<IHubContext<ModelStatusHub>>();
		app.MapHub<ModelStatusHub>("/hubs/model-status");
		await mcpUpstreamRegistry.InitializeAsync(ct);
		app.MapMcp("/mcp");

		// MapFallback rather than Run(...): a plain terminal Run(...) middleware's position in
		// the pipeline relative to the Map* endpoints above depends on ASP.NET Core's implicit
		// middleware auto-ordering, which does NOT reliably run endpoint matching first — MapMcp
		// and MapHub requests were falling through to this catch-all instead of their real
		// handlers. MapFallback is an explicit "only if nothing else matched" endpoint, so
		// precedence is guaranteed regardless of call-site ordering.
		app.MapFallback(context => HandleRequestAsync(context, ct));

		try
		{
			await app.StartAsync(ct);
		}
		catch (Exception ex) when (IsKestrelAddressInUse(ex))
		{
			LogRouterBindFailed(logger, ex, RouterHost, port);
			LogRouterPortInUse(logger);
			return;
		}

		LogRouterServerStarted(logger);
		Console.WriteLine($"Listening on http://{RouterHost}:{port}");
		Console.WriteLine(
			"Endpoints: POST /api/generate, POST /api/chat, POST /v1/chat/completions, POST /chat/completions, POST /api/embeddings, POST /v1/embeddings, POST /embeddings, GET /v1/models, GET /models, GET /api/tags, GET /api/version, GET /health, GET /health/ready, GET /api/capacity, GET /api/system/info");
		Console.WriteLine(
			"Admin Endpoints: GET /admin/config, POST /admin/config, POST /admin/reload (localhost or Bearer token required)");

		onBound?.Invoke();

		try
		{
			await app.WaitForShutdownAsync(ct);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			// Router is shutting down — expected.
		}
		finally
		{
			await app.StopAsync(CancellationToken.None);
		}
	}

	private async Task HandleRequestAsync(HttpContext context, CancellationToken ct = default)
	{
		HttpRequest request = context.Request;
		HttpResponse response = context.Response;

		IPAddress? remoteAddress = context.Connection.RemoteIpAddress;
		bool isLoopback = remoteAddress is not null &&
						 (IPAddress.IsLoopback(remoteAddress) ||
						  remoteAddress.Equals(IPAddress.IPv6Loopback));

		if (!isLoopback && !TrustedNetworks.IsAllowed(remoteAddress))
		{
			// Not loopback and not in trusted networks — require API key
			string? apiKey = ExtractApiKey(request);
			if (!IsValidApiKey(apiKey))
			{
				LogAuthFailed(logger, remoteAddress?.ToString() ?? "unknown");
				await WriteJsonAsync(context, 401, new { error = "unauthorized: missing or invalid API key" });
				return;
			}
		}

		response.Headers.Append("Access-Control-Allow-Origin", "*");
		response.Headers.Append("Access-Control-Allow-Headers", "*");
		response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

		string path = request.Path.Value?.ToUpperInvariant() ?? "/";
		string method = request.Method.ToUpperInvariant();
		string queryString = request.QueryString.Value ?? "";
		string userAgent = request.Headers["User-Agent"].ToString();
		string contentType = request.ContentType ?? "";

		LogRequest(logger, method, path);
		string remoteIp = remoteAddress?.ToString() ?? "unknown-ip";
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
		LogRequestClient(logger, method, path, clientInfo);

		if (!string.IsNullOrEmpty(queryString))
		{
			LogQueryString(logger, queryString);
		}

		LogHeaders(logger, userAgent, contentType);

		// When a request arrives with no User-Agent we can't attribute it the normal way. If the
		// global.debug.logUnattributedRequests diagnostic is enabled, dump the full request
		// fingerprint (every header, remote endpoint, protocol) so we can eyeball whether some OTHER
		// header carries a usable client signal (X-*, Authorization scheme, Accept quirks, a custom
		// app header) worth keying detection off of. Off by default — opt in while investigating.
		if (config.Global.Debug.LogUnattributedRequests && string.IsNullOrWhiteSpace(userAgent))
		{
			LogUnattributedRequestFingerprintImpl(context, method, path, remoteIp);
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

			LogNotFound(logger, method, path);
			await WriteJsonAsync(context, 404, new { error = "not found" });
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			// Router is shutting down mid-request — nothing more to write, the host itself
			// is being torn down. Don't log this as an error.
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
		{
			// The client closed the connection before we finished writing the response.
			// Not a server error — just a client that went away.
			LogClientDisconnected(logger, method, path);
		}
		catch (Exception ex) when (ex is IOException { InnerException: SocketException } or SocketException)
		{
			// Kestrel's connection-reset-while-writing signal — same "client went away"
			// case as the RequestAborted branch above, just surfaced as an exception instead
			// of cancellation depending on exactly where in the write the reset lands.
			LogClientDisconnected(logger, method, path);
		}
		catch (Exception ex)
		{
			// Log the full exception (message + stack trace) server-side for debugging, but
			// don't echo ex.Message back to the HTTP client — this router is normally
			// localhost-only, but exception messages can still incidentally contain file
			// paths, connection strings, or other internal details depending on where the
			// failure originated, and there's no reason for a generic 500 to leak them.
			LogRequestFailed(logger, ex, method, path);

			// A stack trace here means something actually broke — even if Ollama and the
			// models still separately probe as healthy, a caller polling /health should see
			// that reflected. See ModelHealthMonitor.RecordFault for why this is a time-boxed
			// signal rather than something the periodic recovery loop tries to fix.
			health.RecordFault(ex);

			await WriteJsonAsync(context, 500, new { error = "internal server error" });
		}
	}

	private async Task RouteGetAsync(HttpContext context, string path)
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
				await ProxyToOllamaAsync(context, "/api/ps", HttpMethod.Get);
				return;
			case "/API/CAPACITY":
				await HandleCapacityAsync(context);
				return;
			case "/API/SYSTEM/INFO":
				await HandleSystemInfoAsync(context);
				return;
			case "/ADMIN/CONFIG":
				await admin.HandleGetConfigAsync(context);
				return;
			case "/ADMIN/MCP-SERVERS":
				await admin.HandleListMcpServersAsync(context);
				return;
		}

		if (path.StartsWith("/V1/MODELS", StringComparison.Ordinal) || path.StartsWith("/MODELS", StringComparison.Ordinal))
		{
			await WriteJsonAsync(context, 200, BuildModelsListBody());
			return;
		}

		LogGetNotFound(logger, path);
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

	private async Task RoutePostAsync(HttpContext context, string path, CancellationToken ct = default)
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
				await HandleEmbedAsync(context, ct);
				return;
			case "/API/EMBEDDINGS":
			case "/V1/EMBEDDINGS":
			case "/EMBEDDINGS":
				await ProxyEmbeddingsAsync(context, ct);
				return;
			case "/ADMIN/CONFIG":
				await admin.HandleUpdateConfigAsync(context);
				return;
			case "/ADMIN/RELOAD":
				await admin.HandleReloadAsync(context);
				return;
			case "/ADMIN/MCP-SERVERS":
				await admin.HandleAddMcpServerAsync(context);
				return;
			case "/ADMIN/MCP-SERVERS/REMOVE":
				await admin.HandleRemoveMcpServerAsync(context);
				return;
			default:
				string body = await ReadBodyAsync(context.Request);
				string bodyPreview = body.Length > 500 ? body[..500] + "..." : body;
				LogPostNotFound(logger, path, bodyPreview);
				await WriteJsonAsync(context, 404, new { error = "not found" });
				return;
		}
	}

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

	private async Task HandleOllamaGenerateAsync(HttpContext context, CancellationToken ct = default)
	{
		if (await CheckModelWarmupAsync(context))
		{
			return;
		}

		await WaitForModelsReadyAsync(ct);

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
	}

	private async Task HandleOpenAiChatAsync(HttpContext context, CancellationToken ct = default)
	{
		if (await CheckModelWarmupAsync(context))
		{
			return;
		}

		await WaitForModelsReadyAsync(ct);

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

	private async Task HandleOllamaChatAsync(HttpContext context, CancellationToken ct = default)
	{
		if (await CheckModelWarmupAsync(context))
		{
			return;
		}

		await WaitForModelsReadyAsync(ct);

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

	private async Task HandleOpenAiCompletionAsync(HttpContext context, CancellationToken ct = default)
	{
		if (await CheckModelWarmupAsync(context))
		{
			return;
		}

		await WaitForModelsReadyAsync(ct);

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

	// This is the single choke point every POST handler funnels through, so the shared
	// ReadBodyAsync's optional debug logging catches every body exactly once without re-reading
	// the (once-only) input stream or threading the text through handler signatures.
	private Task<string> ReadBodyAsync(HttpRequest request)
		=> RouterHttpUtilities.ReadBodyAsync(request, config, logger);

	/// <summary>
	///     Pass a request straight through to Ollama and stream the response back. Used for
	///     /api/embed and any other endpoints that don't need draft-pair logic. For
	///     <see cref="HttpMethod.Post" />, the router's own request body is forwarded verbatim;
	///     other methods (GET/DELETE/HEAD) send no body. HEAD closes the response without reading
	///     Ollama's response body, matching the semantics of an HTTP HEAD request.
	/// </summary>
	private async Task ProxyToOllamaAsync(HttpContext context, string ollamaPath, HttpMethod method, CancellationToken ct = default)
	{
		using HttpRequestMessage request = new(method, new Uri($"http://127.0.0.1:{process.ServerPort}{ollamaPath}"));
		if (method == HttpMethod.Post)
		{
			string body = await ReadBodyAsync(context.Request);
			request.Content = new StringContent(body, Encoding.UTF8, "application/json");
		}

		using HttpResponseMessage ollamaResponse = await http.SendAsync(request, ct);
		context.Response.StatusCode = (int)ollamaResponse.StatusCode;

		if (method == HttpMethod.Head)
		{
			return;
		}

		context.Response.ContentType = ollamaResponse.Content.Headers.ContentType?.ToString() ?? "application/json";
		byte[] responseBody = await ollamaResponse.Content.ReadAsByteArrayAsync(ct);
		context.Response.ContentLength = responseBody.Length;
		await context.Response.Body.WriteAsync(responseBody, ct);
	}

	/// <summary>
	///     Handle Ollama-native POST /api/embed via the configured generation backend, so it works
	///     the same under LM Studio as it does under Ollama (previously this proxied the request
	///     body verbatim straight to Ollama's own HTTP router, which only ever worked for Ollama).
	/// </summary>
	private async Task HandleEmbedAsync(HttpContext context, CancellationToken ct = default)
	{
		string body = await ReadBodyAsync(context.Request);
		JsonNode? json = JsonNode.Parse(body);
		JsonNode? input = json?["input"];
		string model = json?["model"]?.GetValue<string>() ?? generation.EmbedModelName;

		JsonArray? embeddings = await generation.GetEmbeddingsAsync(model, input, ct);
		if (embeddings is null)
		{
			await WriteJsonAsync(context, 502, new { error = "Embed model unavailable" });
			return;
		}

		await WriteJsonAsync(context, 200, new { model, embeddings });
	}

	/// <summary>
	///     Handle OpenAI-compatible /v1/embeddings via the configured generation backend (Ollama's
	///     /api/embed or LM Studio's /v1/embeddings — see <see cref="SimpleModelPairGenerationService.GetEmbeddingsAsync" />).
	/// </summary>
	private async Task ProxyEmbeddingsAsync(HttpContext context, CancellationToken ct = default)
	{
		string body = await ReadBodyAsync(context.Request);
		JsonNode? json = JsonNode.Parse(body);
		JsonNode? input = json?["input"];
		string model = json?["model"]?.GetValue<string>() ?? generation.EmbedModelName;

		JsonArray? embeddings = await generation.GetEmbeddingsAsync(model, input, ct);
		if (embeddings is null)
		{
			await WriteJsonAsync(context, 502, new { error = "Embed model unavailable" });
			return;
		}

		JsonArray dataArray = [];
		for (int i = 0; i < embeddings.Count; i++)
		{
			dataArray.Add(new JsonObject
			{
				["object"] = "embedding",
				["embedding"] = embeddings[i]?.DeepClone(),
				["index"] = i
			});
		}

		await WriteJsonAsync(context, 200, new
		{
			@object = "list",
			data = dataArray,
			model
		});
	}

	private static Task WriteJsonAsync(HttpContext context, int statusCode, object body)
		=> RouterHttpUtilities.WriteJsonAsync(context, statusCode, body);

	private static async Task WriteRawAsync(HttpResponse response, string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		await response.Body.WriteAsync(bytes);
		await response.Body.FlushAsync();
	}

	// ── IRouter explicit implementations ──

	Task IRouter.HandleHealthAsync(HttpContext context) => HandleHealthAsync(context);

	Task IRouter.HandleHealthReadyAsync(HttpContext context) => HandleHealthReadyAsync(context);

	Task IRouter.HandleCapacityAsync(HttpContext context) => HandleCapacityAsync(context);

	private static string RemoteAddress(HttpContext context)
		=> RouterHttpUtilities.RemoteAddress(context);

	private string? ExtractApiKey(HttpRequest request)
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

}


