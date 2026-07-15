using SimpleModelServer.Common.Infrastructure;
using SimpleModelServer.Common.Interfaces;
using SimpleModelServer.Contracts.Utilities;
using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Hardware;
using SimpleModelServer.Hardware.Interfaces;
using SimpleModelServer.Server.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SimpleModelServer.Server;

/// <summary>
///     Wraps <see cref="SimpleModelPairApp" /> as a <see cref="BackgroundService" /> so the
///     Windows Service Control Manager can start, stop, and restart the Ollama router.
/// </summary>
internal sealed partial class SimpleModelServerWorker(
    AppOptions options,
    AppConfig config,
    ILogger<SimpleModelServerWorker> logger,
    Action<string> trace,
    IHardwareDetection hardwareDetection,
    IGpuBudgetPlanner gpuBudgetPlanner,
    IOllamaHelpers ollamaHelpers,
    IConsoleHelper consoleHelper) : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "SimpleModelServer.Server exited with code {ExitCode}.")]
    private static partial void LogExited(ILogger logger, int exitCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "SimpleModelServer.Server Windows service starting.")]
    private static partial void LogServiceStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SimpleModelServer.Server service stopping gracefully.")]
    private static partial void LogServiceStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "SimpleModelServer.Server encountered a fatal error during startup.")]
    private static partial void LogFatalError(ILogger logger, Exception ex);

    // TEMPORARY DIAGNOSTIC: see the matching comment in Program.cs. `trace` writes straight to
    // a file, bypassing ILogger, so it's visible even under the SCM where Console output and
    // (currently) any ILogger sink both go nowhere. Remove alongside that block once the
    // service-start timeout root cause is fixed.

    // The Windows Service Control Manager gives a service a fixed window (commonly ~30s,
    // see SERVICE_START_PENDING / WaitHint) to report itself as Running before treating the
    // start as failed ("A timeout was reached... while waiting for the service to connect").
    // Ollama health checks, model pulls/warmup, and GPU detection can legitimately take
    // longer than that under a non-interactive service session (Session 0), even when
    // everything is already cached — Session 0 driver/WMI calls and cold TCP probes can be
    // slower than the same calls made from an interactive desktop session.
    //
    // BackgroundService.StartAsync is expected to return quickly so the host can report
    // "started" to the SCM promptly; it does this by not awaiting ExecuteAsync. But if any
    // exception is thrown synchronously (before the first await) inside ExecuteAsync, or if
    // work upstream of it blocks, the SCM can still see the full bring-up sequence as part of
    // the start handshake. To make this robust regardless of exactly where the delay comes
    // from, the entire bring-up (Ollama probe/start, model checks, router bind) is pushed onto
    // a detached background task here, so ExecuteAsync itself returns to the host/SCM almost
    // immediately and never blocks service-start on Ollama/model readiness.
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        trace("SimpleModelServerWorker.ExecuteAsync entered.");
        LogServiceStarting(logger);

        _ = Task.Run(() => RunAppAsync(stoppingToken), stoppingToken);

        trace("SimpleModelServerWorker.ExecuteAsync returning (detached task launched).");
        return Task.CompletedTask;
    }

    private async Task RunAppAsync(CancellationToken stoppingToken)
    {
        trace("RunAppAsync entered (inside detached Task.Run).");
        try
        {
            trace("Constructing SimpleModelPairApp...");
            using SimpleModelPairApp app = new(options, config, logger, hardwareDetection, gpuBudgetPlanner, ollamaHelpers, consoleHelper, trace);
            trace("SimpleModelPairApp constructed. Calling RunAsync...");
            int exitCode = await app.RunAsync(stoppingToken);
            trace($"SimpleModelPairApp.RunAsync returned exitCode={exitCode}.");
            LogExited(logger, exitCode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            trace("RunAppAsync: OperationCanceledException (graceful stop).");
            LogServiceStopping(logger);
        }
        catch (Exception ex)
        {
            trace($"RunAppAsync: FATAL exception: {ex}");
            // Logged only — deliberately not re-thrown. Re-throwing here would propagate out
            // of a detached Task.Run and crash the process via the unobserved/unhandled
            // exception path instead of giving the SCM a clean stop, which is worse than a
            // service that logs a fatal error and exits. The background health loop and
            // per-request error handling inside SimpleModelPairApp already cover ongoing recovery;
            // this catch only guards the initial bring-up sequence.
            LogFatalError(logger, ex);
            Environment.Exit(1);
        }
    }
}
