namespace SimpleModelServer.Contracts.Dtos.Settings;

using SimpleModelServer.Contracts.Dtos.Agents;
using SimpleModelServer.Contracts.Dtos.Providers;
using SimpleModelServer.Contracts.Models;
using SimpleModelServer.Contracts.Providers;

public class GlobalSettings
{
	public string DefaultAgentType { get; set; } = "code";
	public string GpuLayers { get; set; } = "auto";
	public string CpuThreads { get; set; } = "auto";
	/// <summary>ID of the active model configuration (integer reference)</summary>
	public int ConfigurationId { get; set; } = 1;
	public int OllamaPort { get; set; } = 11434;
	public int RouterPort { get; set; } = 11435;
	public string RouterHost { get; set; } = "localhost";
	public string RouterModelName { get; set; } = "HelperModel";
	public string[] TrustedNetworks { get; set; } = [];
	/// <summary>
	///     API keys that are trusted for authentication. Requests from localhost/127.0.0.1 and ::1
	///     are always allowed regardless of API key. Remote requests require a valid API key from this list.
	/// </summary>
	public string[] TrustedApiKeys { get; set; } = [];
	public bool MdnsEnabled { get; set; } = true;
	public string MdnsServiceName { get; set; } = "";
	public string WindowsServiceName { get; set; } = "SimpleModelServerLoader";

	/// <summary>
	///     Model placement preference: Auto (largest practical), GPU (prefer GPU), Size (largest model).
	///     Default is Auto which falls back to Size preference.
	/// </summary>
	public PreferenceMode PreferenceMode { get; set; } = PreferenceMode.Auto;

	/// <summary>GPU capabilities and memory information for model placement decisions</summary>
	public GpuInfo? GpuInfo { get; set; }

	/// <summary>
	///     Provider configuration for model inference backend (Ollama, LM Studio, etc.)
	///     Defaults to Ollama on 127.0.0.1:11434
	/// </summary>
	public ProviderConfiguration? Provider { get; set; } = new()
	{
		Type = SimpleModelServer.Contracts.Providers.Provider.Ollama,
		Host = "127.0.0.1",
		Port = 11434,
	};
}





