using System.Net;
using System.Text;
using System.Linq;

namespace SimpleModelServer.Server.Tests;

using SimpleModelServer.Providers.Ollama;

/// <summary>
///     Minimal stand-in for a real Ollama server, used so tests can exercise
///     <see cref="RouterHttpServer" /> end-to-end over real HTTP without needing an actual Ollama
///     install. Binds an <see cref="HttpListener" /> on 127.0.0.1 and answers just enough of
///     Ollama's native API (<c>/api/generate</c>, <c>/api/embed</c>, <c>/api/ps</c>, plus the
///     bare "is anything listening" probe the health checks use) for
///     <see cref="OllamaProcessManager" />, <see cref="ModelHealthMonitor" />, and
///     <see cref="SimpleModelPairGenerationService" /> to see a healthy backend.
/// </summary>
internal sealed class FakeOllamaServer : IDisposable
{
	private readonly HttpListener listener = new();
	private readonly CancellationTokenSource cts = new();
	private Task? loopTask;
	private bool disposed;

	/// <summary>Response text every /api/generate call streams back (single token + done), unless the request's "model" has an override in <see cref="ResponseTextByModel" />.</summary>
	public string GenerateResponseText { get; set; } = "hello from fake ollama";

	/// <summary>
	///     Per-model override for /api/generate's response text, keyed by the request body's
	///     "model" field. Falls back to <see cref="GenerateResponseText" /> for any model not
	///     listed here. Used by SimpleModelPairMergeTests to give the draft and main models distinct
	///     responses so SimpleModelPairGenerationService.ChooseFinalText's actual selection behavior
	///     (prefer main, fall back to draft only when main comes back empty) can be verified
	///     end-to-end rather than just "some text came back".
	/// </summary>
	public Dictionary<string, string> ResponseTextByModel { get; } = new(StringComparer.Ordinal);

	/// <summary>
	///     Per-model override for how /api/generate streams its response: instead of the default
	///     single-token-then-done behavior, write each string in the list as its own token line,
	///     waiting <c>Delay</c> between writes (none before the first). Lets tests simulate a slow,
	///     multi-chunk draft stream to exercise SimpleModelPairGenerationService's handoff-timeout cutoff
	///     — e.g. only the first chunk or two arrives before a short
	///     <see cref="SimpleModelServer.Config.Configuration.GlobalConfig.DraftHandoffTimeoutMs" /> fires.
	///     Takes precedence over <see cref="ResponseTextByModel" /> for any model listed here.
	/// </summary>
	public Dictionary<string, (string[] Tokens, TimeSpan Delay)> StreamedTokensByModel { get; } =
		new(StringComparer.Ordinal);

	/// <summary>
	///     The most recent /api/generate request's "prompt" field, keyed by model. Lets tests
	///     assert what SimpleModelPairGenerationService actually sent to a model — in particular, whether
	///     the main model's prompt had draft output (or a cleaned-up prompt) spliced into it.
	/// </summary>
	public IReadOnlyDictionary<string, string> LastPromptByModel => lastPromptByModel;

	private readonly Dictionary<string, string> lastPromptByModel = new(StringComparer.Ordinal);
	private readonly Lock lastPromptLock = new();

	/// <summary>When true, /api/generate responds 503 to simulate an unresponsive model.</summary>
	public bool FailGenerate { get; set; }

	/// <summary>
	///     Number of successful /api/generate calls received per model name (parsed from the
	///     request body's "model" field). Used by startup-warmup tests to assert that a specific
	///     set of models (e.g. draft + main + embed) each actually received a warmup call, rather
	///     than just asserting "some generate call happened".
	/// </summary>
	public IReadOnlyDictionary<string, int> GenerateCallsByModel => generateCallsByModel;

	private readonly Dictionary<string, int> generateCallsByModel = new(StringComparer.Ordinal);
	private readonly Lock generateCallsLock = new();

	/// <summary>
	///     Model names /api/ps reports as resident in VRAM. Defaults to null, meaning "report
	///     whatever's asked for as loaded" (via <see cref="HandlePsAsync" />) so existing tests
	///     that never touch this property keep exercising the real generate-probe path rather
	///     than short-circuiting on RouterHttpServer's idle-model check. Set to an explicit list
	///     (including an empty one) to simulate specific models being idled out of VRAM.
	/// </summary>
	public string[]? LoadedModels { get; set; }

	public int Port { get; }

	public FakeOllamaServer()
	{
		Port = GetFreeTcpPort();
		listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
	}

	public void Start()
	{
		listener.Start();
		loopTask = Task.Run(RunLoopAsync);
	}

	private async Task RunLoopAsync()
	{
		while (listener.IsListening && !cts.IsCancellationRequested)
		{
			HttpListenerContext context;
			try
			{
				context = await listener.GetContextAsync().ConfigureAwait(false);
			}
			catch (HttpListenerException)
			{
				break;
			}
			catch (ObjectDisposedException)
			{
				break;
			}

			_ = Task.Run(() => HandleAsync(context), cts.Token);
		}
	}

	private async Task HandleAsync(HttpListenerContext context)
	{
		try
		{
			string path = context.Request.Url?.AbsolutePath.ToUpperInvariant() ?? "/";

			if (FailGenerate && path is "/API/GENERATE")
			{
				context.Response.StatusCode = 503;
				context.Response.Close();
				return;
			}

			switch (path)
			{
				case "/API/GENERATE":
					await HandleGenerateAsync(context).ConfigureAwait(false);
					return;
				case "/API/EMBED":
					await HandleEmbedAsync(context).ConfigureAwait(false);
					return;
				case "/API/PS":
					await HandlePsAsync(context).ConfigureAwait(false);
					return;
				default:
					// Anything else (including the bare readiness probe some callers issue
					// against "/") just needs to prove "something is listening and responding".
					await WriteJsonAsync(context, 200, "{}").ConfigureAwait(false);
					return;
			}
		}
		catch (Exception)
		{
			try
			{
				context.Response.StatusCode = 500;
				context.Response.Close();
			}
			catch
			{
				// best effort
			}
		}
	}

	private async Task HandleEmbedAsync(HttpListenerContext context)
	{
		using StreamReader reader = new(context.Request.InputStream, context.Request.ContentEncoding);
		string body = await reader.ReadToEndAsync().ConfigureAwait(false);

		RecordGenerateCall(body);

		await WriteJsonAsync(context, 200, """{"embeddings":[[0.1,0.2,0.3]]}""").ConfigureAwait(false);
	}

	private async Task HandleGenerateAsync(HttpListenerContext context)
	{
		using StreamReader reader = new(context.Request.InputStream, context.Request.ContentEncoding);
		string body = await reader.ReadToEndAsync().ConfigureAwait(false);
		bool stream = body.Contains("\"stream\":true", StringComparison.Ordinal) ||
					  !body.Contains("\"stream\":false", StringComparison.Ordinal);

		RecordGenerateCall(body);
		RecordLastPrompt(body);
		string? model = ExtractModel(body);

		if (stream && model is not null && StreamedTokensByModel.TryGetValue(model, out (string[] Tokens, TimeSpan Delay) seq))
		{
			context.Response.StatusCode = 200;
			context.Response.ContentType = "application/x-ndjson";
			foreach (string tok in seq.Tokens)
			{
				byte[] line = Encoding.UTF8.GetBytes($$"""{"response":"{{tok}}","done":false}""" + "\n");
				await context.Response.OutputStream.WriteAsync(line).ConfigureAwait(false);
				await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
				if (seq.Delay > TimeSpan.Zero)
				{
					await Task.Delay(seq.Delay).ConfigureAwait(false);
				}
			}

			byte[] streamedDoneLine = Encoding.UTF8.GetBytes("""{"response":"","done":true}""" + "\n");
			await context.Response.OutputStream.WriteAsync(streamedDoneLine).ConfigureAwait(false);
			context.Response.Close();
			return;
		}

		string responseText = ResolveResponseText(body);

		if (!stream)
		{
			await WriteJsonAsync(context, 200,
				$$"""{"response":"{{responseText}}","done":true}""").ConfigureAwait(false);
			return;
		}

		context.Response.StatusCode = 200;
		context.Response.ContentType = "application/x-ndjson";
		byte[] tokenLine =
			Encoding.UTF8.GetBytes($$"""{"response":"{{responseText}}","done":false}""" + "\n");
		byte[] doneLine = Encoding.UTF8.GetBytes("""{"response":"","done":true}""" + "\n");
		await context.Response.OutputStream.WriteAsync(tokenLine).ConfigureAwait(false);
		await context.Response.OutputStream.WriteAsync(doneLine).ConfigureAwait(false);
		context.Response.Close();
	}

	private static string? ExtractModel(string requestBody)
	{
		try
		{
			using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(requestBody);
			return doc.RootElement.TryGetProperty("model", out System.Text.Json.JsonElement modelElement)
				? modelElement.GetString()
				: null;
		}
		catch (System.Text.Json.JsonException)
		{
			return null;
		}
	}

	private void RecordLastPrompt(string requestBody)
	{
		try
		{
			using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(requestBody);
			if (!doc.RootElement.TryGetProperty("model", out System.Text.Json.JsonElement modelElement))
			{
				return;
			}

			string? model = modelElement.GetString();
			if (string.IsNullOrEmpty(model))
			{
				return;
			}

			string prompt = doc.RootElement.TryGetProperty("prompt", out System.Text.Json.JsonElement promptElement)
				? promptElement.GetString() ?? ""
				: "";

			lock (lastPromptLock)
			{
				lastPromptByModel[model] = prompt;
			}
		}
		catch (System.Text.Json.JsonException)
		{
			// Not JSON — nothing to record.
		}
	}

	private string ResolveResponseText(string requestBody)
	{
		try
		{
			using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(requestBody);
			if (doc.RootElement.TryGetProperty("model", out System.Text.Json.JsonElement modelElement))
			{
				string? model = modelElement.GetString();
				if (model is not null && ResponseTextByModel.TryGetValue(model, out string? overrideText))
				{
					return overrideText;
				}
			}
		}
		catch (System.Text.Json.JsonException)
		{
			// Not JSON — fall through to the default.
		}

		return GenerateResponseText;
	}

	private void RecordGenerateCall(string requestBody)
	{
		// Best-effort, deliberately tolerant parse: this is test instrumentation, not production
		// request handling, so a malformed body (e.g. the DegradedHealthTests case that posts
		// "this is not json") should just be skipped rather than blowing up the fake server.
		try
		{
			using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(requestBody);
			if (!doc.RootElement.TryGetProperty("model", out System.Text.Json.JsonElement modelElement))
			{
				return;
			}

			string? model = modelElement.GetString();
			if (string.IsNullOrEmpty(model))
			{
				return;
			}

			lock (generateCallsLock)
			{
				generateCallsByModel[model] = generateCallsByModel.GetValueOrDefault(model) + 1;
			}
		}
		catch (System.Text.Json.JsonException)
		{
			// Not JSON (or missing "model") — nothing to record.
		}
	}

	/// <summary>
	///     Default names SimpleModelPairGenerationService falls back to when config doesn't override
	///     them (see its DraftModelName/MainModelName properties) — used here so tests that never
	///     touch <see cref="LoadedModels" /> still see those models as VRAM-resident, preserving
	///     the real generate-probe path in RouterHttpServer's health check.
	/// </summary>
	private static readonly string[] DefaultLoadedModels = ["qwen2.5-coder-0.5b-chat", "qwen2.5-coder-3b-chat"];

	private async Task HandlePsAsync(HttpListenerContext context)
	{
		string[] loaded = LoadedModels ?? DefaultLoadedModels;
		string modelsJson = string.Join(",", loaded.Select(m => $$"""{"name":"{{m}}"}"""));
		await WriteJsonAsync(context, 200, $$"""{"models":[{{modelsJson}}]}""").ConfigureAwait(false);
	}

	private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, string json)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(json);
		context.Response.StatusCode = statusCode;
		context.Response.ContentType = "application/json";
		context.Response.ContentLength64 = bytes.Length;
		await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
		context.Response.Close();
	}

	private static int GetFreeTcpPort()
	{
		System.Net.Sockets.TcpListener tcpListener = new(IPAddress.Loopback, 0);
		tcpListener.Start();
		int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
		tcpListener.Stop();
		return port;
	}

	public void Dispose()
	{
		// Tests may dispose the fake backend early (to simulate Ollama going down mid-test) and
		// RouterTestFixture.DisposeAsync always disposes it again during teardown.
		if (disposed)
		{
			return;
		}

		disposed = true;

		cts.Cancel();
		try
		{
			listener.Stop();
			listener.Close();
		}
		catch
		{
			// best effort
		}

		try
		{
			loopTask?.Wait(TimeSpan.FromSeconds(2));
		}
		catch
		{
			// best effort
		}

		cts.Dispose();
	}
}


