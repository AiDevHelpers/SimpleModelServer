namespace SimpleModelServer.Server.Infrastructure.Networking;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716",
	Justification = "Stop() matches the conventional start/stop lifecycle naming used throughout this codebase (e.g. TcpListener.Stop, Stopwatch.Stop); renaming for cross-language interop that doesn't apply here would be inconsistent with that convention for no functional benefit.")]
public interface IMdnsAdvertiser : IDisposable
{
	/// <summary>True once <see cref="Advertise" /> has succeeded and <see cref="Stop" /> hasn't been called since.</summary>
	bool IsAdvertising { get; }

	/// <summary>
	///     Registers the service and starts responding to mDNS queries for it. Safe to call more
	///     than once — subsequent calls are a no-op while already advertising.
	/// </summary>
	void Advertise();

	/// <summary>Unregisters the service and stops the underlying mDNS listener. Safe to call when not advertising.</summary>
	void Stop();
}
