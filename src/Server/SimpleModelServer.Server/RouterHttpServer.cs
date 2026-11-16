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
