using System;
using System.Diagnostics;
using System.Text;

namespace SimpleModelServer.Extensions.VS
{
    /// <summary>
    /// Provides diagnostic logging for the extension.
    /// </summary>
    public class DiagnosticLogger
    {
        private readonly string _source;
        private readonly TraceSource? _traceSource;

        public DiagnosticLogger(string source)
        {
            _source = source;
            try
            {
                _traceSource = new TraceSource($"SimpleModelServer.Extensions.VS.{source}");
            }
            catch
            {
                // If trace source fails to initialize, we'll just use Debug output
                _traceSource = null;
            }
        }

        public void Info(string message)
        {
            WriteLog(TraceEventType.Information, message);
        }

        public void Warning(string message)
        {
            WriteLog(TraceEventType.Warning, message);
        }

        public void Error(string message, Exception? ex = null)
        {
            var msg = ex != null ? $"{message}\n{ex}" : message;
            WriteLog(TraceEventType.Error, msg);
        }

        public void Debug(string message)
        {
            WriteLog(TraceEventType.Verbose, message);
        }

        private void WriteLog(TraceEventType eventType, string message)
        {
            var logMessage = $"[{_source}] {message}";

            // Write to trace source if available
            _traceSource?.TraceEvent(eventType, 0, logMessage);

            // Always write to debug output
            System.Diagnostics.Debug.WriteLine(logMessage);

            // Write to trace listeners
            Trace.WriteLine(logMessage, _source);
        }
    }
}
