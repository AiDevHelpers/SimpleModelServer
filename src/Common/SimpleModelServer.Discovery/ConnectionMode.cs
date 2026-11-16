namespace SimpleModelServer.Discovery;

/// <summary>
/// Specifies how the client should connect to the SimpleModelServer instance.
/// </summary>
public enum ConnectionMode
{
	/// <summary>
	/// Connect to a local instance (default port 11435).
	/// </summary>
	Local,

	/// <summary>
	/// Discover via mDNS (DNS-SD).
	/// </summary>
	Mdns,

	/// <summary>
	/// Connect to a manually-specified host and port.
	/// </summary>
	Manual,
}
