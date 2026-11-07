using System.Net;
using Makaretu.Dns;

namespace SimpleModelServer.Discovery;

/// <summary>
/// Locates SimpleModelServer instances using the configured connection mode.
/// Supports Local (http://localhost), Manual (explicit host/port), and mDNS discovery modes.
/// </summary>
public sealed class ServerLocator : IServerLocator
{
	// Must match MdnsAdvertiser.ServiceType in SimpleModelServer.Server
	private const string MdnsServiceType = "_draftmodel._tcp";

	private readonly IServerConnectionStore store;
	private readonly HttpClient httpClient;
	private readonly TimeSpan mdnsTimeout;
	private ResolvedServerConnection? cachedResult;

	/// <summary>
	/// Creates a new ServerLocator instance.
	/// </summary>
	/// <param name="store">The settings store to load/save connection preferences.</param>
	/// <param name="httpClient">Optional HTTP client for local health probes. If null, a default is created.</param>
	/// <param name="mdnsTimeout">Optional timeout for mDNS discovery (default: 3 seconds).</param>
	public ServerLocator(IServerConnectionStore store, HttpClient? httpClient = null, TimeSpan? mdnsTimeout = null)
	{
		this.store = store ?? throw new ArgumentNullException(nameof(store));
		this.httpClient = httpClient ?? new HttpClient();
		this.mdnsTimeout = mdnsTimeout ?? TimeSpan.FromSeconds(3);
	}

	/// <summary>
	/// Asynchronously resolves the server connection, using cached result if available.
	/// </summary>
	public async Task<ResolvedServerConnection> ResolveAsync(CancellationToken cancellationToken = default)
	{
		if (cachedResult is not null)
		{
			return cachedResult;
		}

		return cachedResult = await ResolveInternalAsync(cancellationToken);
	}

	/// <summary>
	/// Asynchronously rediscovers the server connection, clearing any cached result.
	/// </summary>
	public async Task<ResolvedServerConnection> RediscoverAsync(CancellationToken cancellationToken = default)
	{
		cachedResult = null;
		return await ResolveAsync(cancellationToken);
	}

	private async Task<ResolvedServerConnection> ResolveInternalAsync(CancellationToken cancellationToken)
	{
		var settings = await store.LoadAsync(cancellationToken);

		return settings.Mode switch
		{
			ConnectionMode.Local => await ResolveLocalAsync(settings, cancellationToken),
			ConnectionMode.Manual => ResolveManual(settings),
			ConnectionMode.Mdns => await ResolveMdnsAsync(settings, cancellationToken),
			_ => throw new InvalidOperationException($"Unknown connection mode: {settings.Mode}"),
		};
	}

	private async Task<ResolvedServerConnection> ResolveLocalAsync(ServerConnectionSettings settings, CancellationToken cancellationToken)
	{
		var port = settings.ManualPort > 0 ? settings.ManualPort : 11435;
		var url = $"http://localhost:{port}/health";

		try
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(TimeSpan.FromSeconds(2));

			var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
			if (response.IsSuccessStatusCode)
			{
				return new ResolvedServerConnection
				{
					BaseUrl = $"http://localhost:{port}",
					IsLoopback = true,
					ApiKey = null,
					ResolvedVia = ConnectionMode.Local,
				};
			}
		}
		catch (OperationCanceledException)
		{
			// Timeout or cancellation — fall through to mDNS
		}
		catch (HttpRequestException)
		{
			// Connection refused or similar — fall through to mDNS
		}

		// Fallback to mDNS if local probe fails
		return await ResolveMdnsAsync(settings, cancellationToken);
	}

	private ResolvedServerConnection ResolveManual(ServerConnectionSettings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.ManualHost))
		{
			throw new InvalidOperationException("Manual connection mode requires ManualHost to be set.");
		}

		var port = settings.ManualPort > 0 ? settings.ManualPort : 11435;
		var baseUrl = $"http://{settings.ManualHost}:{port}";
		var isLoopback = IsLoopbackAddress(settings.ManualHost);

		return new ResolvedServerConnection
		{
			BaseUrl = baseUrl,
			IsLoopback = isLoopback,
			ApiKey = isLoopback ? null : settings.ApiKey,
			ResolvedVia = ConnectionMode.Manual,
		};
	}

	private async Task<ResolvedServerConnection> ResolveMdnsAsync(ServerConnectionSettings settings, CancellationToken cancellationToken)
	{
		var services = await BrowseMdnsAsync(cancellationToken);

		if (services.Count == 0)
		{
			throw new InvalidOperationException("No SimpleModelServer instance found via mDNS within the timeout.");
		}

		// Prefer remembered instance, otherwise use first responder
		var selectedService = services.FirstOrDefault(s => s.InstanceName == settings.RememberedMdnsInstanceName);
		if (selectedService == default)
		{
			selectedService = services[0];
		}

		return new ResolvedServerConnection
		{
			BaseUrl = $"http://{selectedService.Hostname}:{selectedService.Port}",
			IsLoopback = false,
			ApiKey = settings.ApiKey,
			ResolvedVia = ConnectionMode.Mdns,
		};
	}

	private sealed class DiscoveredService
	{
		public required string InstanceName { get; init; }
		public required string Hostname { get; init; }
		public required ushort Port { get; init; }
	}

	private async Task<List<DiscoveredService>> BrowseMdnsAsync(CancellationToken cancellationToken)
	{
		Dictionary<string, DiscoveredService> results = new(StringComparer.OrdinalIgnoreCase);

		try
		{
			using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutCts.CancelAfter(mdnsTimeout);

			using var mdns = new MulticastService();
			using var discovery = new ServiceDiscovery(mdns);

			void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs args)
			{
				DiscoveredService? found = ExtractHostAndPort(args);
				if (found is not null)
				{
					results[found.InstanceName] = found;
				}
			}

			discovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;

			try
			{
				mdns.Start();
				discovery.QueryServiceInstances(MdnsServiceType);

				// Collect responses for the full timeout window — multiple instances may
				// answer, and each answer can arrive at a different time.
				await Task.Delay(mdnsTimeout, timeoutCts.Token);
			}
			finally
			{
				discovery.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
				mdns.Stop();
			}
		}
		catch (OperationCanceledException)
		{
			// Timeout is expected — return whatever was collected before it fired.
		}
		catch (Exception)
		{
			// If mDNS discovery fails for any reason, return whatever was collected so far.
			// The caller will handle an empty result appropriately.
		}

		return [.. results.Values];
	}

	/// <summary>
	/// Pulls the SRV target/port and matching A-record address out of the discovery response.
	/// mDNS responders (including <c>MdnsAdvertiser</c>) send the SRV/TXT/A records for an
	/// instance alongside the PTR answer in the same message, so everything needed is present
	/// on the event args without a follow-up query.
	/// </summary>
	private static DiscoveredService? ExtractHostAndPort(ServiceInstanceDiscoveryEventArgs args)
	{
		string instanceName = args.ServiceInstanceName.ToString();

		IEnumerable<ResourceRecord> records = args.Message.Answers.Concat(args.Message.AdditionalRecords);

		SRVRecord? srv = records.OfType<SRVRecord>()
			.FirstOrDefault(r => r.Name.ToString().Equals(instanceName, StringComparison.OrdinalIgnoreCase));
		if (srv is null)
		{
			return null;
		}

		string target = srv.Target.ToString();
		AddressRecord? address = records.OfType<AddressRecord>()
			.FirstOrDefault(r => r.Name.ToString().Equals(target, StringComparison.OrdinalIgnoreCase));

		string hostname = address?.Address.ToString() ?? target;

		return new DiscoveredService
		{
			InstanceName = instanceName,
			Hostname = hostname,
			Port = srv.Port,
		};
	}

	private static bool IsLoopbackAddress(string host)
	{
		return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
			|| host == "127.0.0.1"
			|| host == "::1";
	}
}
