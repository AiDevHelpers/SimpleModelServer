using System.Net;
using System.Net.Sockets;

namespace SimpleModelServer.Server.Infrastructure.Networking;

/// <summary>
///     Represents one entry in <c>global.trustedNetworks</c>: an IPv4 network expressed as a
///     dotted-quad with trailing zero octets implying the mask width. This matches the
///     shorthand people commonly use when describing a subnet by its network address alone:
///     <list type="bullet">
///         <item><c>192.168.0.0</c>  → mask 255.255.0.0   (/16, matches 192.168.x.x)</item>
///         <item><c>192.168.1.0</c>  → mask 255.255.255.0 (/24, matches 192.168.1.x)</item>
///         <item><c>10.0.0.0</c>     → mask 255.0.0.0     (/8,  matches 10.x.x.x)</item>
///         <item><c>127.0.0.1</c>    → mask 255.255.255.255 (/32, exact host match)</item>
///     </list>
///     The mask width is derived from the position of the first octet that starts a trailing
///     run of zero octets, scanning from the right: as many trailing ".0" octets as the address
///     has are treated as "don't care" positions. An address with no trailing zero octets (e.g.
///     127.0.0.1, where the last octet is non-zero) is treated as a single-host /32 match.
///     A bare "*" or empty string also matches any address.
/// </summary>
public readonly struct TrustedNetwork : IEquatable<TrustedNetwork>
{
	private readonly uint networkValue;
	private readonly uint maskValue;
	private readonly bool matchAll;

	private TrustedNetwork(uint network, uint mask, bool matchAll)
	{
		networkValue = network;
		maskValue = mask;
		this.matchAll = matchAll;
	}

	/// <summary>The original config string this entry was parsed from.</summary>
	public string Source { get; private init; } = "";

	public bool Equals(TrustedNetwork other)
	{
		return networkValue == other.networkValue &&
			   maskValue == other.maskValue &&
			   matchAll == other.matchAll &&
			   string.Equals(Source, other.Source, StringComparison.Ordinal);
	}

	public override bool Equals(object? obj)
	{
		return obj is TrustedNetwork other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(networkValue, maskValue, matchAll, Source);
	}

	public static bool operator ==(TrustedNetwork left, TrustedNetwork right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(TrustedNetwork left, TrustedNetwork right)
	{
		return !left.Equals(right);
	}

	/// <summary>
	///     Parses a single trustedNetworks entry. Returns null if the string is not a
	///     recognizable IPv4 address or wildcard.
	/// </summary>
	public static TrustedNetwork? Parse(string entry)
	{
		string trimmed = entry.Trim();
		if (trimmed.Length == 0 || trimmed == "*")
		{
			return new TrustedNetwork(0, 0, true) { Source = entry };
		}

		// Allow explicit CIDR form too ("192.168.1.0/24") for callers who prefer it, even
		// though the primary documented form is the trailing-zero-octet shorthand.
		int slashIndex = trimmed.IndexOf('/', StringComparison.Ordinal);
		if (slashIndex >= 0)
		{
			string addrPart = trimmed[..slashIndex];
			string prefixPart = trimmed[(slashIndex + 1)..];
			if (!IPAddress.TryParse(addrPart, out IPAddress? cidrAddr) ||
				cidrAddr.AddressFamily != AddressFamily.InterNetwork ||
				!int.TryParse(prefixPart, out int prefixLen) ||
				prefixLen is < 0 or > 32)
			{
				return null;
			}

			uint cidrNetwork = ToUInt32(cidrAddr);
			uint cidrMask = prefixLen == 0 ? 0u : 0xFFFFFFFFu << (32 - prefixLen);
			return new TrustedNetwork(cidrNetwork & cidrMask, cidrMask, false) { Source = entry };
		}

		if (!IPAddress.TryParse(trimmed, out IPAddress? address) ||
			address.AddressFamily != AddressFamily.InterNetwork)
		{
			return null;
		}

		byte[] octets = address.GetAddressBytes();

		// Count trailing zero octets (from the right) to determine how many octets are
		// "wildcarded". 192.168.1.0 -> 1 trailing zero octet -> /24. 192.168.0.0 -> 2 trailing
		// zero octets -> /16. 127.0.0.1 -> 0 trailing zero octets (last octet is 1) -> /32.
		// 0.0.0.0 -> all four zero -> treated as match-all, same as "*".
		int trailingZeroOctets = 0;
		for (int i = 3; i >= 0; i--)
		{
			if (octets[i] != 0)
			{
				break;
			}

			trailingZeroOctets++;
		}

		if (trailingZeroOctets == 4)
		{
			return new TrustedNetwork(0, 0, true) { Source = entry };
		}

		int maskBits = (4 - trailingZeroOctets) * 8;
		uint mask = maskBits == 0 ? 0u : 0xFFFFFFFFu << (32 - maskBits);
		uint network = ToUInt32(address) & mask;

		return new TrustedNetwork(network, mask, false) { Source = entry };
	}

	/// <summary>True if <paramref name="candidate" /> falls within this network.</summary>
	public bool Contains(IPAddress candidate)
	{
		if (matchAll)
		{
			return true;
		}

		IPAddress normalized = NormalizeToIPv4(candidate);
		if (normalized.AddressFamily != AddressFamily.InterNetwork)
		{
			return false;
		}

		uint candidateValue = ToUInt32(normalized);
		return (candidateValue & maskValue) == networkValue;
	}

	/// <summary>
	///     Maps IPv4-mapped IPv6 addresses (::ffff:a.b.c.d, as seen from dual-stack listeners)
	///     back to their IPv4 form so trustedNetworks entries written as IPv4 still match.
	/// </summary>
	private static IPAddress NormalizeToIPv4(IPAddress address)
	{
		return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
	}

	private static uint ToUInt32(IPAddress address)
	{
		byte[] bytes = address.GetAddressBytes();
		return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
	}
}

/// <summary>
///     A parsed collection of <see cref="TrustedNetwork" /> entries used to gate router access.
///     An empty list means "no restriction" — every request is allowed, preserving the
///     historical default behavior for configs that don't set trustedNetworks at all.
/// </summary>
public sealed class TrustedNetworkList : ITrustedNetworkList
{
	private readonly TrustedNetwork[] networks;

	private TrustedNetworkList(TrustedNetwork[] networks)
	{
		this.networks = networks;
	}

	/// <summary>No trustedNetworks configured — every address is allowed.</summary>
	public static TrustedNetworkList Unrestricted { get; } = new([]);

	/// <summary>True when at least one network entry is configured (restriction is active).</summary>
	public bool IsRestricted => networks.Length > 0;

	public static TrustedNetworkList FromConfig(IEnumerable<string> entries)
	{
		List<TrustedNetwork> parsed = [];
		foreach (string entry in entries)
		{
			TrustedNetwork? network = TrustedNetwork.Parse(entry);
			if (network is { } value)
			{
				parsed.Add(value);
			}
		}

		return parsed.Count == 0 ? Unrestricted : new TrustedNetworkList([.. parsed]);
	}

	/// <summary>
	///     True if the request should be allowed: either no restriction is configured, or
	///     <paramref name="address" /> matches at least one configured network.
	/// </summary>
	public bool IsAllowed(IPAddress? address)
	{
		if (!IsRestricted)
		{
			return true;
		}

		if (address is null)
		{
			return false;
		}

		// Always allow loopback regardless of configured networks — a misconfigured
		// trustedNetworks list should never be able to lock out the local machine itself.
		if (IPAddress.IsLoopback(address))
		{
			return true;
		}

		foreach (TrustedNetwork network in networks)
		{
			if (network.Contains(address))
			{
				return true;
			}
		}

		return false;
	}
}
