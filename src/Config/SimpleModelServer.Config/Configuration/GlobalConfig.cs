using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed class GlobalConfig : IGlobalConfig
{
	public string DefaultAgentType { get; set; } = "code";
	public HardwareSetting GpuLayers { get; set; } = HardwareSetting.Unspecified;
	public HardwareSetting CpuThreads { get; set; } = HardwareSetting.Unspecified;
	public string DraftModel { get; set; } = "";

	/// <summary>
	///     Which <c>models.configurations</c> entry the structured schema resolves to (see
	///     <see cref="AppConfig" />). Default 1, matching the stock config.
	/// </summary>
	public int ConfigurationId { get; set; } = 1;
	public string MainModel { get; set; } = "";

	/// <summary>
	///     Embedding model for semantic search. Empty = fall back to the 'embed' model role in config.json, then
	///     'nomic-embed-text'.
	/// </summary>
	public string EmbedModel { get; set; } = "";

	public int OllamaPort { get; set; } = 11434;
	public int RouterPort { get; set; } = 11435;

	/// <summary>
	///     Hostname or IP the router HttpListener binds to. Default "localhost".
	///     Use "*" or "+" to bind all interfaces (requires admin / URL ACL).
	/// </summary>
	public string RouterHost { get; set; } = "localhost";

	public string RouterModelName { get; set; } = "HelperModel";
	public int? ContextSize { get; set; }
	public string AgentType { get; set; } = "";

	/// <summary>
	///     How long (in milliseconds) SimpleModelPairGenerationService lets the draft (0.5b) model
	///     stream before handing off to the main (3b) model, whatever the draft has produced so
	///     far. This is a *handoff cutoff*, not a wait-for-completion timeout: if the draft
	///     finishes sooner, handoff happens immediately; if it's still going, whatever text has
	///     streamed by this point is what gets spliced into the main model's prompt (see
	///     <see cref="Configuration.DraftMode" />), and the rest of the draft's output is drained
	///     in the background and discarded. Default 900ms.
	/// </summary>
	public int DraftHandoffTimeoutMs { get; set; } = 900;

	/// <summary>
	///     What the draft (0.5b) model's output is used for before main (3b) runs. See
	///     <see cref="Configuration.DraftMode" /> for the three options. Default
	///     <see cref="Configuration.DraftMode.Preview" /> preserves the original draft-pair
	///     behavior.
	/// </summary>
	public DraftMode DraftMode { get; set; } = DraftMode.Preview;

	/// <summary>
	///     Legacy: path to separate model-sources file. Ignored when SimpleModelServer.config.json
	///     contains an inline "models" section.
	/// </summary>
	public string ModelSourcesPath { get; init; } = "";

	/// <summary>
	///     IPv4 networks allowed to connect to the router, expressed as dotted-quad network
	///     addresses whose trailing zero octets imply the mask width (e.g. "192.168.0.0" for a
	///     /16, "192.168.1.0" for a /24 — see <see cref="SimpleModelServer.Server.Infrastructure.Networking.TrustedNetwork" />).
	///     CIDR form ("10.0.0.0/8") is also accepted. Empty (the default) means unrestricted —
	///     every client is allowed, matching prior behavior. Loopback is always allowed
	///     regardless of this setting.
	/// </summary>
	public IReadOnlyList<string> TrustedNetworks { get; set; } = [];

	/// <summary>
	///     API keys that are trusted for authentication. Requests from localhost/127.0.0.1
	///     are always allowed regardless of API key. Remote requests require a valid API key from this list.
	/// </summary>
	public IReadOnlyList<string> TrustedApiKeys { get; set; } = [];

	/// <summary>
	///     When true (the default), the router advertises itself over mDNS
	///     (<c>_draftmodel._tcp.local.</c>) so LAN clients can discover host/port without a
	///     hardcoded config — see <see cref="SimpleModelServer.Library.Networking.MdnsAdvertiser" /> and
	///     docs/mdns-discovery.md. Set to false to disable (e.g. locked-down networks where
	///     multicast is blocked or undesirable).
	/// </summary>
	public bool MdnsEnabled { get; set; } = true;

	/// <summary>
	///     Overrides the advertised mDNS service instance name. Empty (the default) uses
	///     "SimpleModelServer {hostname}" with the hostname sanitized per
	///     <see cref="SimpleModelServer.Library.Networking.MdnsAdvertiser.SafeInstanceLabel" />.
	/// </summary>
	public string MdnsServiceName { get; set; } = "";

	/// <summary>
	///     Name of the Windows service this process runs under, purely for logging when
	///     <see cref="SimpleModelServer.Loader.ModelHealthMonitor" /> escalates to a full process restart
	///     after sustained health-check failures survive Ollama-only recovery (see
	///     ModelHealthMonitor.RequestProcessRestart / RestartWindowsServiceOrExitProcess). Defaults
	///     to "SimpleModelServerLoader" to match the name both
	///     build/scripts/install-simplemodelserver-service.ps1 and build/wix/SimpleModelServer.Setup/Service.wxs
	///     register it under — those installers also configure
	///     <c>sc failure SimpleModelServerLoader reset=86400 actions=restart/5000/restart/10000/restart/30000</c>,
	///     so the actual restart mechanism is just this process exiting non-zero; there's nothing
	///     to invoke via this name beyond the log line unless a future deployment mechanism needs it.
	/// </summary>
	public string WindowsServiceName { get; init; } = "SimpleModelServerLoader";

	/// <summary>
	///     Opt-in diagnostic toggles (config <c>global.debug</c>). Off by default — enable
	///     individual flags while investigating, e.g. to fingerprint unattributed clients. See
	///     <see cref="DebugConfig" />.
	/// </summary>
	public IDebugConfig Debug { get; init; } = new DebugConfig();

	/// <summary>
	///     Which inference backend serves generation requests. See <see cref="IGlobalConfig.Provider" />.
	/// </summary>
	public IProviderConfiguration Provider { get; init; } = new ProviderConfiguration();

	/// <summary>
	///     Minimum log level for all outputs. Default "Information".
	///     Serilog levels: "Verbose", "Debug", "Information", "Warning", "Error", "Fatal".
	/// </summary>
	public string LogLevel { get; init; } = "Information";

	/// <summary>
	///     When true, logs are also written to a file at <see cref="LogPath" />.
	///     Default false — file logging is opt-in.
	/// </summary>
	public bool EnableFileLogs { get; init; } = false;

	/// <summary>
	///     Path to the log file when <see cref="EnableFileLogs" /> is true.
	///     Empty = use the default path ({ProgramData}\SimpleModelServer\logs\log-.txt).
	///     Supports template placeholders like {Date:yyyyMMdd}.
	/// </summary>
	public string LogPath { get; init; } = "";
}
