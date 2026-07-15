using SimpleModelServer.Config;
using SimpleModelServer.Common.Infrastructure;
using SimpleModelServer.Common.Interfaces;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Hardware;
using SimpleModelServer.Hardware.Interfaces;
using SimpleModelServer.Providers.Common.Utilities;
using SimpleModelServer.Server;
using SimpleModelServer.Server.Configuration;
using SimpleModelServer.Server.Infrastructure;
using IConfigurationManager = SimpleModelServer.Server.Configuration.IConfigurationManager;
using IConfigurationUpdater = SimpleModelServer.Server.Configuration.IConfigurationUpdater;
using IAdminAuthenticationService = SimpleModelServer.Server.Configuration.IAdminAuthenticationService;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using System.Diagnostics;

bool runningAsService = WindowsServiceHelpers.IsWindowsService();
bool verboseLogging = Environment.GetEnvironmentVariable("SIMPLEMODELSERVER_VERBOSE_LOGGING") is "1" or "true";

AppOptions options = AppOptions.Parse(args);

if (options.InstallService)
{
    return InstallWindowsService();
}

if (options.UninstallService)
{
    return UninstallWindowsService();
}

// When the SCM launches the exe it runs non-interactively.
// UseWindowsService() detects this and wires up SCM lifetime management.
if (WindowsServiceHelpers.IsWindowsService())
{
    // TEMPORARY DIAGNOSTIC LOGGING (see the "service failed to start" / SCM 30s timeout
    // investigation): Console-based ILogger output is invisible under the SCM — there's no
    // console for a service session, so every logger.Log* call above this point vanishes
    // silently. This writes directly to a plain file via File.AppendAllText, bypassing the
    // ILogger/DI pipeline entirely, so it still captures a trace even if something in host
    // construction, DI, or the hosted service registration itself is what's slow or throwing.
    // Remove once the root cause of the 1920/SERVICE_START timeout is found and fixed.
    string traceLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SimpleModelServer", "service-startup-trace.log");
    void Trace(string message)
    {
        try
        {
            string? dir = Path.GetDirectoryName(traceLogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                _ = Directory.CreateDirectory(dir);
            }

            File.AppendAllText(traceLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostic logging must never itself be the reason startup fails/slows further.
        }
    }

    Trace("=== Service process started ===");
    Trace($"IsWindowsService()=true, args=[{string.Join(" ", args)}]");

    string configPath2 = options.ConfigPath ?? DefaultConfig.LoaderConfigPath();
    Trace($"Resolved config path: {configPath2}");

    DefaultConfig.EnsureConfig(configPath2);
    Trace("EnsureConfig done.");

    DefaultConfig.EnsureModelSources(DefaultConfig.ModelSourcesPath(configPath2));
    Trace("EnsureModelSources done.");

    AppConfig svcConfig = AppConfig.Load(configPath2);
    Trace("AppConfig.Load done.");

    Trace("Building host...");

    // Configure Serilog with all sinks (console, event log, named pipe, optional file)
    Serilog.ILogger svcLogger = SerilogSetup.ConfigureLogger(svcConfig, runningAsService: true, verboseLogging);

    IHost host = Host.CreateDefaultBuilder(args)
        .UseSerilog(svcLogger)
        .UseWindowsService(o => o.ServiceName = "SimpleModelServerLoader")
        .ConfigureServices((_, services) =>
        {
            // NOTE: these previously had bogus `(HostBuilderContext)` casts on
            // IServiceCollection-returning calls (AddSingleton/AddHostedService return
            // IServiceCollection, never HostBuilderContext) — that cast always threw
            // InvalidCastException synchronously inside this delegate, which runs during
            // Host.Build() itself, i.e. before host.RunAsync() and before SimpleModelServerWorker's
            // own try/catch ever got a chance to run. This is the exact failure mode documented
            // in INSTALLER_REQUIREMENTS.md's "Known pitfalls" #3 — it was still live here, not
            // just historical. See also the CLAUDE.md pitfall entry on SCM-service startup
            // failures for how invisible this was without the Trace() calls above pinpointing
            // "Building host..." as the last line ever logged.
            services.AddSingleton(options);
            services.AddSingleton(svcConfig);
            services.AddSingleton(Trace);
            services.AddSingleton<IAppConfig>(svcConfig);
            services.AddSingleton<IHardwareDetection, HardwareDetection>();
            services.AddSingleton<IGpuBudgetPlanner, GpuBudgetPlanner>();
            services.AddSingleton<IOllamaHelpers, OllamaHelpers>();
            services.AddSingleton<IConsoleHelper, ConsoleHelper>();
            services.AddSingleton<SystemInfoService>();
            // Configuration management services for admin API
            services.AddSingleton<IConfigurationManager, ConfigurationManager>();
            services.AddSingleton<IConfigurationUpdater>(sp =>
                new ConfigurationUpdater(sp.GetRequiredService<IConfigurationManager>()));
            services.AddSingleton<IAdminAuthenticationService, AdminAuthenticationService>();
            services.AddSingleton(sp => new McpUpstreamRegistry(svcConfig.McpServers, sp.GetRequiredService<ILogger<McpUpstreamRegistry>>()));
            services.AddSingleton<AdminEndpointHandler>();
            services.AddHostedService<SimpleModelServerWorker>();
        })
        .Build();
    Trace("Host built. Calling host.RunAsync()...");

    await host.RunAsync();
    Trace("host.RunAsync() returned (service stopping).");
    return 0;
}

if (options.Help)
{
    Console.WriteLine("""

                      USAGE
                        SimpleModelServer.Server [options]

                      DESCRIPTION
                        Ensures a valid, healthy local Ollama instance is running with the
                        required models loaded and ready. Runs the ServiceRouter for
                        TokenSaver.MCP and other consumers.

                      COMMANDS
                        --service                Start Ollama + Router (default)
                        --router                 Start Router only (assumes Ollama already running)
                        --kill                   Stop all Ollama processes
                        --help                   Show this help

                      OPTIONS
                        --router-port <int>      Override config routerPort
                        --config <path>          Config JSON path (default: .tokensaver/SimpleModelServer.config.json
                                                 under SIMPLEMODELSERVER_WORKSPACE, or alongside the exe)

                      ADMIN API
                        GET  /admin/config          Get current configuration (JSON)
                        POST /admin/config          Update a configuration setting
                        POST /admin/reload          Reload hot-reloadable settings
                        GET  /admin/mcp-servers     List configured MCP upstream servers
                        POST /admin/mcp-servers     Add/update an MCP upstream server
                        POST /admin/mcp-servers/remove  Remove an MCP upstream server

                        Localhost (127.0.0.1) is always trusted.
                        Remote access requires Bearer token: Authorization: Bearer <key>
                        See docs/ADMIN_CONFIG.md for complete API reference.

                      CONFIG FILE (SimpleModelServer.config.json)
                        global.routerPort        Router listen port (default 11435)
                        global.routerModelName   Virtual model name advertised by router
                        global.ollamaPort        Ollama server port (default 11434)
                        global.draftModel        Draft model name
                        global.mainModel         Main model name
                        global.contextSize       Default num_ctx
                        global.defaultAgentType  Default agent: ask, code, debug, planning
                        global.agentType         Force a specific agent type
                        global.trustedApiKeys    API keys for remote client authentication
                                                 (localhost is always trusted, no key required)
                        global.trustedNetworks   Networks allowed without API key (CIDR notation)
                        global.provider          Model inference provider configuration:
                                                 type: "Ollama" or "LMStudio" (default: Ollama)
                                                 host: server address (default: 127.0.0.1)
                                                 port: server port (default: 11434 for Ollama, 1234 for LMStudio)
                                                 apiKey: optional authentication key for remote providers
                        global.mdnsEnabled       Advertise router over mDNS (default true)
                        global.mdnsServiceName   Override advertised mDNS instance name
                        global.draftMode         Draft usage: Preview, Cleanup, or Both
                        global.draftHandoffTimeoutMs  Draft handoff timeout in milliseconds

                      WORKSPACE INTEGRATION
                        Set SIMPLEMODELSERVER_WORKSPACE to the workspace root. The config file and
                        .tokensaver/ folder will be created there automatically.

                      DOCUMENTATION
                        Quick start:    docs/ADMIN_QUICKSTART.md
                        Full reference: docs/ADMIN_CONFIG.md
                        Overview:       docs/README_ADMIN.md

                      """);
    return 0;
}

try
{
    string configPath = options.ConfigPath ?? DefaultConfig.LoaderConfigPath();
    DefaultConfig.EnsureConfig(configPath);
    DefaultConfig.EnsureModelSources(DefaultConfig.ModelSourcesPath(configPath));
    IAppConfig config = AppConfig.Load(configPath);

    // Configure Serilog for interactive (non-service) mode
    Serilog.ILogger serilogLogger = SerilogSetup.ConfigureLogger(config, runningAsService: false, verboseLogging);
    var loggerFactory = new SerilogLoggerFactory(serilogLogger);
    Microsoft.Extensions.Logging.ILogger appLogger = loggerFactory.CreateLogger<Program>();

    IHardwareDetection hardwareDetection = new HardwareDetection();
    IGpuBudgetPlanner gpuBudgetPlanner = new GpuBudgetPlanner();
    IOllamaHelpers ollamaHelpers = new OllamaHelpers();
    IConsoleHelper consoleHelper = new ConsoleHelper();
    using SimpleModelPairApp app = new(options, config, appLogger, hardwareDetection, gpuBudgetPlanner, ollamaHelpers, consoleHelper);
    return await app.RunAsync();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nFATAL: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Console.ResetColor();
    Console.WriteLine("\nPress Enter to close...");
    _ = Console.ReadLine();
    return 1;
}

// ── Windows Service helpers ───────────────────────────────────────────────────

static int InstallWindowsService()
{
    string exePath = Process.GetCurrentProcess().MainModule?.FileName
                  ?? throw new InvalidOperationException("Cannot determine exe path.");

    Console.WriteLine($"Installing SimpleModelServerLoader service → {exePath}");

    string configPath = DefaultConfig.LoaderConfigPath();

    int r = RunSc(
        $"create SimpleModelServerLoader binpath= \"{exePath}\" start= auto DisplayName= \"Simple Model Server\"");
    if (r != 0)
    {
        return r;
    }

    r = RunSc(
        $"config SimpleModelServerLoader binpath= \"{exePath} --service --config {configPath}\"");
    if (r != 0)
    {
        return r;
    }

    _ = RunSc(
        "description SimpleModelServerLoader \"Manages the local Ollama instance and exposes a draft-pair router for AI coding tools.\"");

    // Auto-recover: restart after 5s, 10s, 30s
    _ = RunSc("failure SimpleModelServerLoader reset= 86400 actions= restart/5000/restart/10000/restart/30000");

    r = RunSc("start SimpleModelServerLoader");
    if (r == 0)
    {
        Console.WriteLine("Service installed and started.");
    }

    Console.WriteLine("\nPress Enter to close...");
    _ = Console.ReadLine();
    return r;
}

static int UninstallWindowsService()
{
    Console.WriteLine("Stopping SimpleModelServerLoader service...");
    _ = RunSc("stop SimpleModelServerLoader");
    Console.WriteLine("Deleting SimpleModelServerLoader service...");
    int result = RunSc("delete SimpleModelServerLoader");
    Console.WriteLine("\nPress Enter to close...");
    _ = Console.ReadLine();
    return result;
}

static int RunSc(string args)
{
    using Process p = Process.Start(new ProcessStartInfo("sc.exe", args)
    {
        UseShellExecute = false,
        CreateNoWindow = true
    })!;
    p.WaitForExit();
    Console.WriteLine($"  sc.exe {args}  →  exit {p.ExitCode}");
    return p.ExitCode;
}

