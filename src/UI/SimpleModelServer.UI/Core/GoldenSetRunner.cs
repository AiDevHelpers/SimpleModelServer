using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleModelServer.UI.Core
{
    /// <summary>Outcome of a golden-set eval run.</summary>
    public sealed record GoldenSetResult(bool Started, int ExitCode, string Output);

    /// <summary>
    /// Runs the SimpleModelServer.Evals CLI (router mode) against the currently configured
    /// server, so a developer can validate an agent prompt change before trusting it.
    /// </summary>
    public class GoldenSetRunner
    {
        private readonly SettingsManager _settings;
        private readonly DiagnosticLogger _logger;

        public GoldenSetRunner(SettingsManager settings, DiagnosticLogger? logger = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? new DiagnosticLogger("GoldenSetRunner");
        }

        /// <summary>
        /// Launches "SimpleModelServer.Evals --mode router --router-url &lt;server&gt;" and waits
        /// for it to finish, returning its combined output. If the executable can't be found
        /// (e.g. <see cref="SettingsManager.EvalsExecutablePath"/> is unset or wrong), returns a
        /// result with Started=false and an explanatory message.
        /// </summary>
        public async Task<GoldenSetResult> RunRouterEvalAsync(CancellationToken cancellationToken = default)
        {
            var exePath = _settings.EvalsExecutablePath;
            var routerUrl = _settings.GetServerUrl();

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--mode router --router-url \"{routerUrl}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to start golden set eval ({exePath}): {ex.Message}");
                return new GoldenSetResult(
                    Started: false,
                    ExitCode: -1,
                    Output: $"Could not launch '{exePath}'. Set the Evals executable path in settings.\n{ex.Message}");
            }

            Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Process may have already exited
                }
                throw;
            }

            output.Append(await stdOutTask);
            var stdErr = await stdErrTask;
            if (!string.IsNullOrEmpty(stdErr))
            {
                output.Append('\n').Append(stdErr);
            }

            _logger.Info($"Golden set eval exited with code {process.ExitCode}");
            return new GoldenSetResult(Started: true, ExitCode: process.ExitCode, Output: output.ToString());
        }
    }
}
