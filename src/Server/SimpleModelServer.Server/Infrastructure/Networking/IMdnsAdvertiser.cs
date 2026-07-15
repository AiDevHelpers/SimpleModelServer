namespace SimpleModelServer.Server.Infrastructure.Networking;

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
