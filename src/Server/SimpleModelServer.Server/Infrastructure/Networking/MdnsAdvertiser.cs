using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

using Makaretu.Dns;

namespace SimpleModelServer.Server.Infrastructure.Networking;

/// <summary>
///     Advertises the SimpleModelServer.Loader router over mDNS (RFC 6762 / DNS-SD) so other tools on
///     the LAN — the VS Code extension, other agent adapters, a future discovery-aware
///     TokenSaver.MCP — can find it without hardcoding host/port.
///
///     This mirrors the pattern RomRaider's ECU-Toolkit uses for its own peer discovery
///     (<c>ECU-Toolkit/src/ecu_def_toolkit/sync/discovery.py</c>, built on Python's
///     <c>zeroconf</c> package): a single advertised service instance with a small TXT-record
///     payload identifying the app, plus the same "skip virtual/VPN/hypervisor adapters when
///     picking the LAN IP" heuristic. Unlike ECU-Toolkit's <c>SyncAdvertiser</c>/<c>SyncBrowser</c>
///     pair, this class is advertise-only — SimpleModelServer.Loader does not browse for or forward
///     requests to peer instances (see docs/mdns-discovery.md for the scope decision).
/// </summary>
public sealed class MdnsAdvertiser(string hostname, int port, string? serviceInstanceName = null)
	: IMdnsAdvertiser
{
	/// <summary>
	///     Dedicated service type rather than reusing the generic <c>_http._tcp.local.</c> type
	///     ECU-Toolkit advertises under — Simple Model Server's router is a specific API surface (draft-pair
	///     generation, not a generic web server), so giving it its own DNS-SD type lets mDNS
	///     browsers (dns-sd, Bonjour Browser, avahi-browse) distinguish it from any other local
	///     HTTP service without needing to inspect TXT records first.
	/// </summary>
	public const string ServiceType = "_draftmodel._tcp";

	public const string AppId = "draftmodel-loader";
	private const string ServiceVersion = "0.1";

	// Same rationale as ECU-Toolkit's discovery.py _SKIP_NAME: Hyper-V/Docker/VPN adapters
	// routinely appear in the interface list and would otherwise be picked over the real LAN
	// adapter, advertising an address nothing else on the network can actually reach.
	private static readonly Regex SkipAdapterName = new(
		@"(loopback|virtual|vethernet|vmware|vbox|bluetooth|tunnel|teredo" +
		@"|isatap|6to4|pseudo|hyper.?v|wsl|npcap|parallels|tailscale" +
		@"|zerotier|radmin|hamachi|vpn|tun\d|tap\d)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private readonly string serviceInstanceName = string.IsNullOrWhiteSpace(serviceInstanceName)
		? $"SimpleModelServer {SafeInstanceLabel(hostname)}"
		: serviceInstanceName;

	private MulticastService? mdns;
	private ServiceDiscovery? discovery;
	private ServiceProfile? profile;
	private bool advertising;

	/// <summary>True once <see cref="Advertise" /> has succeeded and <see cref="Stop" /> hasn't been called since.</summary>
	public bool IsAdvertising => advertising;

	/// <summary>
	///     Strip dots and any character invalid in a DNS-SD instance label so the advertised name
	///     is always well-formed, regardless of what <c>Environment.MachineName</c> returns.
	///     Mirrors discovery.py's <c>_safe_hostname</c>.
	/// </summary>
	public static string SafeInstanceLabel(string rawHostname)
	{
		string firstLabel = rawHostname.Split('.')[0];
		string sanitized = Regex.Replace(firstLabel, "[^a-zA-Z0-9-]", "-");
		return sanitized.Length > 63 ? sanitized[..63] : sanitized;
	}

	/// <summary>
	///     Registers the service and starts responding to mDNS queries for it. Safe to call more
	///     than once — subsequent calls are a no-op while already advertising.
	/// </summary>
	public void Advertise()
	{
		if (advertising)
		{
			return;
		}

		mdns = new MulticastService();
		discovery = new ServiceDiscovery(mdns);

		profile = new ServiceProfile(serviceInstanceName, ServiceType, (ushort)port, [ResolveLocalAddress()]);
		profile.AddProperty("app", AppId);
		profile.AddProperty("hostname", SafeInstanceLabel(hostname));
		profile.AddProperty("version", ServiceVersion);
		profile.AddProperty("path", "/health");

		discovery.Advertise(profile);
		mdns.Start();
		advertising = true;
	}

	/// <summary>Unregisters the service and stops the underlying mDNS listener. Safe to call when not advertising.</summary>
	public void Stop()
	{
		if (!advertising)
		{
			return;
		}

		try
		{
			if (profile is not null)
			{
				discovery?.Unadvertise(profile);
			}
		}
		catch
		{
			// Best-effort — matches discovery.py's async_stop/stop swallowing unregister
			// failures: a teardown-time mDNS send failure should never surface as an error to
			// the caller shutting the router down.
		}

		discovery?.Dispose();
		mdns?.Dispose();
		discovery = null;
		mdns = null;
		profile = null;
		advertising = false;
	}

	public void Dispose()
	{
		Stop();
	}

	/// <summary>
	///     Picks the best physical LAN IPv4 address, skipping virtual/VPN/hypervisor adapters —
	///     same heuristic and priority order (192.168.x/10.x preferred over other private ranges)
	///     as discovery.py's <c>_get_local_ip</c>. Falls back to <see cref="System.Net.IPAddress.Loopback" />
	///     if nothing suitable is found (advertising loopback is still useful for same-machine
	///     discovery/testing, it just won't be reachable from other hosts).
	/// </summary>
	private static System.Net.IPAddress ResolveLocalAddress()
	{
		List<(int Priority, System.Net.IPAddress Address)> candidates = [];

		foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
		{
			if (nic.OperationalStatus != OperationalStatus.Up)
			{
				continue;
			}

			if (SkipAdapterName.IsMatch(nic.Name) || SkipAdapterName.IsMatch(nic.Description))
			{
				continue;
			}

			foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
			{
				if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
				{
					continue;
				}

				if (System.Net.IPAddress.IsLoopback(addr.Address))
				{
					continue;
				}

				byte[] octets = addr.Address.GetAddressBytes();
				if (IsLinkLocal(octets) || IsHyperVOrDockerNat(octets))
				{
					continue;
				}

				int priority = octets[0] == 192 && octets[1] == 168 || octets[0] == 10 ? 0 : 1;
				candidates.Add((priority, addr.Address));
			}
		}

		if (candidates.Count == 0)
		{
			return System.Net.IPAddress.Loopback;
		}

		candidates.Sort((a, b) => a.Priority.CompareTo(b.Priority));
		return candidates[0].Address;
	}

	private static bool IsLinkLocal(byte[] octets)
	{
		return octets[0] == 169 && octets[1] == 254;
	}

	/// <summary>172.16.0.0/12 — squatted on entirely by Hyper-V Default Switch / Docker NAT, never a real LAN.</summary>
	private static bool IsHyperVOrDockerNat(byte[] octets)
	{
		return octets[0] == 172 && octets[1] is >= 16 and <= 31;
	}
}
