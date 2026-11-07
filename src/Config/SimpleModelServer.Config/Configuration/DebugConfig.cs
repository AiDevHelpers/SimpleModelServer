using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

/// <summary>
///     Opt-in diagnostic toggles under config <c>global.debug</c>. Each flag is independent and
///     defaults to false so normal operation stays quiet; turn one on to get extra logging for a
///     specific investigation, then turn it back off. Add new one-off diagnostics here rather than
///     scattering ad-hoc <c>#if DEBUG</c> or always-on verbose logging through the code.
/// </summary>
public sealed class DebugConfig : IDebugConfig
{
	/// <summary>
	///     When true, any request that arrives with no <c>User-Agent</c> header is logged with its
	///     full fingerprint (every header — Authorization redacted to its scheme — plus remote
	///     endpoint, IP, HTTP version, keep-alive). Used to discover whether an unidentified client
	///     (e.g. a background embeddings indexer) carries some other usable signal. Off by default.
	/// </summary>
	public bool LogUnattributedRequests { get; init; }

	/// <summary>
	///     When true, POST request bodies are logged (truncated) as they're routed. Verbose and
	///     potentially sensitive — intended only for short local debugging sessions. Off by default.
	/// </summary>
	public bool LogRequestBodies { get; init; }
}
