using System;
using System.Diagnostics;

namespace SimpleModelServer.UI.Core
{
	/// <summary>
	/// Provides diagnostic logging for Simple Model Server UI.
	/// </summary>
	public class DiagnosticLogger
	{
		private readonly string _source;
		private readonly TraceSource? _traceSource;

		/// <summary>Creates a logger that tags each entry with the given source name.</summary>
		public DiagnosticLogger(string source)
		{
			_source = source;
			try
			{
				_traceSource = new TraceSource($"SimpleModelServer.UI.{source}");
			}
			catch
			{
				// If trace source fails to initialize, we'll just use Debug output
				_traceSource = null;
			}
		}

		/// <summary>Writes an informational message.</summary>
		public void Info(string message)
		{
			WriteLog(TraceEventType.Information, message);
		}

		/// <summary>Writes a warning message.</summary>
		public void Warning(string message)
		{
			WriteLog(TraceEventType.Warning, message);
		}

		/// <summary>Writes an error message, with the exception when one is given.</summary>
		public void Error(string message, Exception? ex = null)
		{
			var msg = ex != null ? $"{message}\n{ex}" : message;
			WriteLog(TraceEventType.Error, msg);
		}

		/// <summary>Writes a debug message.</summary>
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
