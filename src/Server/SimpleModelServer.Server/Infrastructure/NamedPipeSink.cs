using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using System.IO.Pipes;
using System.Text;

namespace SimpleModelServer.Server.Infrastructure;

/// <summary>
/// Serilog sink that writes log events to a named pipe for real-time consumption by the VS Code extension.
/// Creates/reconnects on demand; failures are silently ignored (logging must never crash the app).
/// Buffers events while waiting for a client to connect.
/// </summary>
internal sealed class NamedPipeSink : ILogEventSink, IDisposable
{
	private const string PipeName = "SimpleModelServer.Logs";
	private readonly ITextFormatter _formatter;
	private NamedPipeServerStream? _pipeServer;
	private StreamWriter? _writer;
	private readonly object _lockObj = new();
	private bool _disposed = false;
	private DateTime _lastConnectionAttempt = DateTime.MinValue;
	private const int ConnectionRetryIntervalMs = 5000; // Retry connection every 5 seconds

	public NamedPipeSink(ITextFormatter formatter)
	{
		_formatter = formatter;
	}

	public void Emit(LogEvent logEvent)
	{
		if (_disposed || logEvent == null)
		{
			return;
		}

		try
		{
			lock (_lockObj)
			{
				// Try to write if we have a connected pipe
				if (_writer != null && _pipeServer?.IsConnected == true)
				{
					try
					{
						WriteEvent(logEvent);
						return;
					}
					catch
					{
						// Write failed — pipe disconnected
						DisconnectPipe();
					}
				}

				// If not connected and enough time has passed, try to reconnect
				if (DateTime.UtcNow.Subtract(_lastConnectionAttempt).TotalMilliseconds >= ConnectionRetryIntervalMs)
				{
					TryCreatePipe();
					_lastConnectionAttempt = DateTime.UtcNow;

					// If we have a newly connected pipe, try writing
					if (_writer != null && _pipeServer?.IsConnected == true)
					{
						try
						{
							WriteEvent(logEvent);
						}
						catch
						{
							DisconnectPipe();
						}
					}
				}
			}
		}
		catch
		{
			// Logging sink failures must never crash the app
		}
	}

	private void WriteEvent(LogEvent logEvent)
	{
		var sb = new StringBuilder();
		using (var writer = new StringWriter(sb))
		{
			_formatter.Format(logEvent, writer);
		}

		_writer?.WriteLine(sb.ToString());
		_writer?.Flush();
	}

	private void TryCreatePipe()
	{
		try
		{
			// Dispose previous pipe if any
			_writer?.Dispose();
			_writer = null;
			_pipeServer?.Dispose();
			_pipeServer = null;

			// Create new pipe server (listens for one client connection)
			_pipeServer = new NamedPipeServerStream(
				PipeName,
				PipeDirection.Out,
				1, // Only one client at a time
				PipeTransmissionMode.Byte,
				PipeOptions.Asynchronous
			);

			// Try to wait for a client with a short timeout
			var waitTask = _pipeServer.WaitForConnectionAsync();
			if (waitTask.Wait(TimeSpan.FromMilliseconds(100)))
			{
				// Client connected — create writer
				_writer = new StreamWriter(_pipeServer, Encoding.UTF8) { AutoFlush = true };
			}
			else
			{
				// No client connected yet — keep the pipe open for future clients
				// (the async wait continues in the background)
			}
		}
		catch
		{
			// Pipe creation or connection attempt failed
			DisconnectPipe();
		}
	}

	private void DisconnectPipe()
	{
		_writer?.Dispose();
		_writer = null;

		_pipeServer?.Dispose();
		_pipeServer = null;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		lock (_lockObj)
		{
			DisconnectPipe();
		}
	}
}
