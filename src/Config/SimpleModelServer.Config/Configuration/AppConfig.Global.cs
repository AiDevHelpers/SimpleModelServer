using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed partial class AppConfig
{
	private static GlobalConfig ParseGlobal(JsonObject global)
	{
		return new GlobalConfig
		{
			DefaultAgentType =
				GetString(global, "defaultAgentType", "default_agent_type", "defaultAgent", "default_agent") ?? "code",
			GpuLayers = GetHardwareSetting(global, "gpuLayers", "gpu_layers", "numGpu", "num_gpu"),
			CpuThreads = GetHardwareSetting(global, "cpuThreads", "cpu_threads", "numThread", "num_thread"),
			DraftModel = GetString(global, "draftModel", "draft_model") ?? "",
			ConfigurationId = GetInt(global, "configurationId", "configuration_id") ?? 1,
			MainModel = GetString(global, "mainModel", "main_model") ?? "",
			EmbedModel = GetString(global, "embedModel", "embed_model") ?? "",
			OllamaPort = GetInt(global, "ollamaPort", "ollama_port") ?? 11434,
			RouterPort = GetInt(global, "routerPort", "router_port") ?? 11435,
			RouterModelName = GetString(global, "routerModelName", "router_model_name") ?? "HelperModel",
			ContextSize = GetInt(global, "contextSize", "context_size"),
			AgentType = GetString(global, "agentType", "agent_type") ?? "",
			RouterHost = GetString(global, "routerHost", "router_host") ?? "localhost",
			DraftHandoffTimeoutMs = GetInt(global, "draftHandoffTimeoutMs", "draft_handoff_timeout_ms") ?? 900,
			DraftMode = ParseDraftMode(GetString(global, "draftMode", "draft_mode")),
			// Kept for backward compat — ignored when primary config has inline "models" section
			ModelSourcesPath = GetString(global, "modelSourcesPath", "model_sources_path") ?? "",
			TrustedNetworks = ParseStringArray(GetNode(global, "trustedNetworks", "trusted_networks")),
			TrustedApiKeys = ParseStringArray(GetNode(global, "trustedApiKeys", "trusted_api_keys")),
			MdnsEnabled = GetBool(global, "mdnsEnabled", "mdns_enabled") ?? true,
			MdnsServiceName = GetString(global, "mdnsServiceName", "mdns_service_name") ?? "",
			WindowsServiceName = GetString(global, "windowsServiceName", "windows_service_name") ?? "",
			Debug = ParseDebug(GetObject(global, "debug") ?? []),
			Provider = ParseProvider(GetObject(global, "provider") ?? []),
			LogLevel = GetString(global, "logLevel", "log_level") ?? "Information",
			EnableFileLogs = GetBool(global, "enableFileLogs", "enable_file_logs") ?? false,
			LogPath = GetString(global, "logPath", "log_path") ?? ""
		};
	}

	/// <summary>
	///     Parses the optional <c>global.debug</c> object. Every flag defaults to false (off) so a
	///     config with no "debug" section — or an empty one — keeps normal, quiet behavior; these are
	///     opt-in diagnostics you enable while investigating, not steady-state logging.
	/// </summary>
	private static DebugConfig ParseDebug(JsonObject debug)
	{
		return new DebugConfig
		{
			LogUnattributedRequests =
				GetBool(debug, "logUnattributedRequests", "log_unattributed_requests") ?? false,
			LogRequestBodies =
				GetBool(debug, "logRequestBodies", "log_request_bodies") ?? false
		};
	}

	/// <summary>
	///     Parses the optional <c>global.provider</c> object, defaulting to Ollama on 127.0.0.1:11434
	///     when absent so every existing config (which predates this section entirely) keeps working
	///     unchanged.
	/// </summary>
	private static ProviderConfiguration ParseProvider(JsonObject provider)
	{
		// The serializer writes enums as numbers by default (what DefaultConfig emits), so accept
		// the ordinal as well as the name. Anything unrecognized falls back to Ollama.
		Provider type = ParseProviderType(GetNode(provider, "type")) ?? Provider.Ollama;

		return new ProviderConfiguration
		{
			Type = type,
			Host = GetString(provider, "host") ?? "127.0.0.1",
			Port = GetInt(provider, "port") ?? (type == Provider.LMStudio ? 1234 : 11434),
			ApiKey = GetString(provider, "apiKey", "api_key"),
			TimeoutSeconds = GetInt(provider, "timeoutSeconds", "timeout_seconds")
		};
	}

	private static Provider? ParseProviderType(JsonNode? node)
	{
		if (node is not JsonValue value)
		{
			return null;
		}

		if (value.TryGetValue(out int ordinal) && Enum.IsDefined(typeof(Provider), ordinal))
		{
			return (Provider)ordinal;
		}

		return value.TryGetValue(out string? text) && Enum.TryParse(text, ignoreCase: true, out Provider parsed)
			? parsed
			: null;
	}

	/// <summary>
	///     Parses <c>global.draftMode</c>, defaulting to <see cref="DraftMode.Preview" /> for any
	///     missing/unrecognized value so existing configs (and every test that never sets this)
	///     keep the original draft-answer-preview behavior unchanged.
	/// </summary>
	private static DraftMode ParseDraftMode(string? value)
	{
		return value?.Trim().ToUpperInvariant() switch
		{
			"CLEANUP" => Configuration.DraftMode.Cleanup,
			"BOTH" => Configuration.DraftMode.Both,
			"NONE" => Configuration.DraftMode.None,
			"VERIFY" => Configuration.DraftMode.Verify,
			_ => Configuration.DraftMode.Preview
		};
	}

	private static string[] ParseStringArray(JsonNode? node)
	{
		if (node is not JsonArray arr)
		{
			return [];
		}

		List<string> result = [];
		foreach (JsonNode? item in arr)
		{
			string? s = item?.GetValue<string>();
			if (!string.IsNullOrWhiteSpace(s))
			{
				result.Add(s);
			}
		}

		return result.ToArray();
	}

}
