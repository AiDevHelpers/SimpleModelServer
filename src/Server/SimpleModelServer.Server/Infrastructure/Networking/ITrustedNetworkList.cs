namespace SimpleModelServer.Server.Infrastructure.Networking;

using System.Net;

public interface ITrustedNetworkList
{
	/// <summary>True when at least one network entry is configured (restriction is active).</summary>
	bool IsRestricted { get; }

	/// <summary>
	///     True if the request should be allowed: either no restriction is configured, or
	///     <paramref name="address" /> matches at least one configured network.
	/// </summary>
	bool IsAllowed(IPAddress? address);
}
