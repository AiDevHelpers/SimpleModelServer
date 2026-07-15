using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using SimpleModelServer.Config.Configuration;

namespace SimpleModelServer.Server.Infrastructure;

/// <summary>
/// Configures Serilog with console, event log, and named pipe sinks.
/// </summary>
internal static class SerilogSetup
{
    /// <summary>
    /// Configures and returns a Serilog logger.
    /// </summary>
    /// <param name="config">Application configuration</param>
    /// <param name="runningAsService">True if running as a Windows service</param>
    /// <param name="verboseLogging">True for Debug level (SIMPLEMODELSERVER_VERBOSE_LOGGING env var)</param>
    /// <returns>Configured ILogger</returns>
    public static ILogger ConfigureLogger(IAppConfig config, bool runningAsService, bool verboseLogging = false)
    {
        var minLevel = verboseLogging ? LogEventLevel.Debug : LogEventLevel.Information;

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .Enrich.WithProperty("MachineName", Environment.MachineName);

        // Console sink with colorization
        loggerConfig = loggerConfig.WriteTo.Console(
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code
        );

        // Named pipe sink for real-time VS Code extension consumption
        var pipeFormatter = new CompactJsonFormatter();
        loggerConfig = loggerConfig.WriteTo.Sink(new NamedPipeSink(pipeFormatter));

        // Windows Event Log sink for service events (starts, stops, crashes, errors)
        if (runningAsService && OperatingSystem.IsWindows())
        {
            try
            {
                loggerConfig = loggerConfig.WriteTo.EventLog(
                    source: "SimpleModelServer",
                    restrictedToMinimumLevel: LogEventLevel.Warning
                );
            }
            catch
            {
                // Event log setup failure should not crash startup
            }
        }

        return loggerConfig.CreateLogger();
    }
}
