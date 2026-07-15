using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;

using Microsoft.Extensions.Logging;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Providers.Ollama;

/// <summary>
///     Owns the lifecycle of the local Ollama server process: starting it, killing stuck
///     instances, waiting for readiness, probing health, and ensuring required models are
///     pulled/created. This is deliberately narrow — it knows nothing about the HTTP router,
///     agent types, or draft-pair generation; those are separate responsibilities layered on
///     top by <see cref="SimpleModelPairApp" />.
/// </summary>
public sealed partial class OllamaProcessManager(IAppConfig config, ILogger logger, HttpClient sharedHttp) : IOllamaProcessManager
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Checking model '{Model}'...")]
    private static partial void LogCheckingModel(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Model '{Model}' already installed.")]
    private static partial void LogModelAlreadyInstalled(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Creating model '{Model}' from '{Base}'...")]
    private static partial void LogCreatingModel(ILogger logger, string model, string @base);

    [LoggerMessage(Level = LogLevel.Information, Message = "Model '{Model}' created.")]
    private static partial void LogModelCreated(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for server on port {Port}...")]
    private static partial void LogWaitingForServer(ILogger logger, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Killing any stuck Ollama processes...")]
    private static partial void LogKillingOllama(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Killing {ProcessName} (PID {Pid})")]
    private static partial void LogKillingProcess(ILogger logger, string processName, int pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not kill {ProcessName} (PID {Pid}).")]
    private static partial void LogCouldNotKillProcess(ILogger logger, Exception ex, string processName, int pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pulling model '{Model}'...")]
    private static partial void LogPullingModel(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Starting Ollama server on port {Port}...")]
    private static partial void LogStartingServer(ILogger logger, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server READY.")]
    private static partial void LogServerReady(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Server did not start.")]
    private static partial void LogServerDidNotStart(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not unload model '{Model}' ({StatusCode}). Continuing.")]
    private static partial void LogCouldNotUnload(ILogger logger, string model, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not unload model '{Model}': {Message}")]
    private static partial void LogCouldNotUnloadError(ILogger logger, Exception ex, string model, string message);

    private static readonly TimeSpan ProbeHttpClientTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ServerReadyPollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ServerReadyTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ModelResponsiveCheckTimeout = TimeSpan.FromSeconds(3);

    // UseProxy: false — this client only ever talks to localhost (the local Ollama process), so
    // there's nothing to proxy. Without it, the default HttpClientHandler's system proxy
    // auto-detection (WPAD) can add multiple seconds of latency to the *first* request on some
    // networks — enough to blow through ModelResponsiveCheckTimeout and misreport a perfectly
    // healthy model as unresponsive/warming-up.
    private readonly HttpClient probeHttp = new(new HttpClientHandler { UseProxy = false }) { Timeout = ProbeHttpClientTimeout };
    private Process? ollamaProcess;
    private bool disposed;

    public int ServerPort => config.Global.OllamaPort;

    public static bool CommandExists(string command)
    {
        string[] paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        string[] extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD").Split(';')
            : [""];

        return paths
            .SelectMany(path => extensions.Select(ext => Path.Combine(path, command + ext)))
            .Any(File.Exists);
    }

    public void RegisterExistingOllamaProcess()
    {
        // Process.GetProcessesByName returns a live OS handle per match; if more than one
        // "ollama" process happens to be running, only the first is kept (and eventually
        // disposed via ollamaProcess?.Dispose() in Dispose()) — the rest must be disposed here
        // or their handles leak for the lifetime of this app.
        Process[] matches = Process.GetProcessesByName("ollama");
        ollamaProcess = matches.Length > 0 ? matches[0] : null;
        for (int i = 1; i < matches.Length; i++)
        {
            matches[i].Dispose();
        }
    }

    public int? OllamaProcessId => ollamaProcess?.Id;

    public void KillStuckOllama()
    {
        LogKillingOllama(logger);
        // "ollama" is the server (ollama.exe). "ollama app" is the Windows tray/desktop app
        // (ollama app.exe → process name "ollama app", with a space) — it auto-relaunches
        // `ollama serve`, so leaving it alive means it immediately respawns the very process we
        // just killed and fights the router-managed instance for the port (the endless
        // "listen tcp :11434: bind: ... forbidden" churn in Ollama's server.log). "ollama_app"
        // is kept as a harmless legacy alias.
        foreach (string name in new[] { "ollama", "ollama app", "ollama_app" })
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        LogKillingProcess(logger, process.ProcessName, process.Id);
                        process.Kill(true);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // InvalidOperationException: process already exited between enumeration
                        // and Kill(). Win32Exception: access denied / OS-level failure killing
                        // it. Both are expected/recoverable — log instead of a bare swallow so
                        // a persistently-stuck process is at least visible in the logs.
                        LogCouldNotKillProcess(logger, ex, process.ProcessName, process.Id);
                    }
                }
            }
        }
    }

    public async Task EnsureModelAsync(string model)
    {
        LogCheckingModel(logger, model);
        string list = await RunProcessCaptureAsync("ollama", "list");
        if (list.Contains(model, StringComparison.OrdinalIgnoreCase))
        {
            LogModelAlreadyInstalled(logger, model);
            return;
        }

        if (config.ModelSources.TryGetValue(model, out IModelSourceConfig? source) && !string.IsNullOrWhiteSpace(source.Base))
        {
            LogCreatingModel(logger, model, source.Base);
            JsonObject payload = new() { ["name"] = model, ["from"] = source.Base };
            JsonObject parameters = [];
            if (source.NumCtx is not null)
            {
                parameters["num_ctx"] = source.NumCtx.Value;
            }

            if (source.Stop.Count > 0)
            {
                JsonArray arr = [.. source.Stop];

                parameters["stop"] = arr;
            }

            if (parameters.Count > 0)
            {
                payload["parameters"] = parameters;
            }

            using JsonContent createContent = JsonContent.Create(payload);
            using HttpResponseMessage response =
                await sharedHttp.PostAsync(new Uri($"http://127.0.0.1:{ServerPort}/api/create"), createContent);
            _ = response.EnsureSuccessStatusCode();
            LogModelCreated(logger, model);
            return;
        }

        LogPullingModel(logger, model);
        _ = await RunProcessCaptureAsync("ollama", $"pull {model}", true);
    }

    public async Task<bool> StartServerAsync()
    {
        LogStartingServer(logger, ServerPort);
        ProcessStartInfo startInfo = new()
        {
            FileName = "ollama",
            Arguments = "serve",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["OLLAMA_HOST"] = $"http://127.0.0.1:{ServerPort}";
        startInfo.Environment["OLLAMA_MODELS"] = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SimpleModelServer", "models");

        ollamaProcess = Process.Start(startInfo);
        return await WaitForServerAsync();
    }

    public static bool IsAddressInUse(HttpListenerException ex)
    {
        // ERROR_SHARING_VIOLATION (32) or ERROR_ALREADY_EXISTS (183) style conflicts from
        // HttpListener manifest as ErrorCode 32 or 183 on Windows. Fall back to matching the
        // message text so we still handle this gracefully if the OS reports a different code.
        return ex.ErrorCode is 32 or 183
            || ex.Message.Contains("conflicts with an existing registration", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("already in use", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Address already in use", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsOllamaReadyAsync()
    {
        try
        {
            using HttpResponseMessage response = await probeHttp.GetAsync(new Uri($"http://127.0.0.1:{ServerPort}/api/tags"));
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

    public Task<bool> IsModelResponsiveAsync(string model)
    {
        return IsModelResponsiveAsync(model, ModelResponsiveCheckTimeout);
    }

    /// <summary>
    ///     True if <paramref name="model" /> is currently resident in VRAM per Ollama's
    ///     <c>/api/ps</c>. False here does NOT mean unhealthy — it just means keep_alive expired
    ///     and idled it out; the next request reloads it. Callers use this to distinguish that
    ///     idle case from an actually-unresponsive model in <see cref="IsModelResponsiveAsync" />.
    /// </summary>
    public async Task<bool> IsModelLoadedInVramAsync(string model)
    {
        try
        {
            using HttpResponseMessage response =
                await probeHttp.GetAsync(new Uri($"http://127.0.0.1:{ServerPort}/api/ps"));
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            JsonNode? body = await response.Content.ReadFromJsonAsync<JsonNode>();
            JsonArray? models = body?["models"]?.AsArray();
            return models?.Any(m => string.Equals(m?["name"]?.GetValue<string>(), model, StringComparison.Ordinal)) ??
                   false;
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

    public async Task<bool> IsModelResponsiveAsync(string model, TimeSpan timeout)
    {
        try
        {
            using CancellationTokenSource cts = new(timeout);
            // Embedding-only models (e.g. nomic-embed-text) don't support /api/generate — Ollama
            // returns a non-success status for it, which would otherwise misreport a perfectly
            // healthy embed model as unresponsive on every probe.
            bool isEmbed = config.IsEmbedModel(model);
            Uri endpoint = new($"http://127.0.0.1:{ServerPort}/{(isEmbed ? "api/embed" : "api/generate")}");
            using JsonContent request = JsonContent.Create(isEmbed
                ? new JsonObject { ["model"] = model, ["input"] = "hi", ["keep_alive"] = 0 }
                : new JsonObject { ["model"] = model, ["prompt"] = "hi", ["stream"] = false, ["keep_alive"] = 0 });
            using HttpResponseMessage response = await probeHttp.PostAsync(endpoint, request, cts.Token);
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

    public async Task<bool> WaitForServerAsync()
    {
        LogWaitingForServer(logger, ServerPort);
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(ServerReadyTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await IsOllamaReadyAsync())
            {
                LogServerReady(logger);
                return true;
            }

            await Task.Delay(ServerReadyPollInterval);
        }

        LogServerDidNotStart(logger);
        return false;
    }

    public async Task UnloadModelAsync(string model)
    {
        try
        {
            bool isEmbed = config.IsEmbedModel(model);
            Uri endpoint = new($"http://127.0.0.1:{ServerPort}/{(isEmbed ? "api/embed" : "api/generate")}");
            using JsonContent request = JsonContent.Create(isEmbed
                ? new JsonObject { ["model"] = model, ["input"] = "", ["keep_alive"] = 0 }
                : new JsonObject { ["model"] = model, ["prompt"] = "", ["stream"] = false, ["keep_alive"] = 0 });
            using HttpResponseMessage response = await sharedHttp.PostAsync(endpoint, request);
            if (!response.IsSuccessStatusCode)
            {
                LogCouldNotUnload(logger, model, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            LogCouldNotUnloadError(logger, ex, model, ex.Message);
        }
    }

    private static async Task<string> RunProcessCaptureAsync(string fileName, string arguments, bool echo = false)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using Process process = Process.Start(startInfo) ??
                            throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        string output = await outputTask;
        string error = await errorTask;
        if (echo)
        {
            await Console.Out.WriteAsync(output);
            await Console.Error.WriteAsync(error);
        }

        return process.ExitCode != 0 ? throw new InvalidOperationException($"{fileName} {arguments} failed: {error}") : output;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        probeHttp.Dispose();
        ollamaProcess?.Dispose();
        disposed = true;
    }
}




