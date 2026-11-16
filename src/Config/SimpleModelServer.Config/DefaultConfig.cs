using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimpleModelServer.Common.Configuration;
using SimpleModelServer.Contracts;

namespace SimpleModelServer.Config;

public static class DefaultConfig
{

	/// <summary>
	///     Kept for backward compatibility — written only if the legacy file is missing and
	///     callers explicitly need it. New installations use <see cref="StockJson" /> which embeds models.
	/// </summary>
	public static string StockModelSourcesJson => StockJson;

	private static string SerializeStockConfig()
	{
		var config = CreateStockConfig();
		var options = new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true,
		};
		return JsonSerializer.Serialize(config, options);
	}

	private const string FileName = "SimpleModelServer.config.json";

	/// <summary>Legacy separate model-sources filename. Still read as a fallback if present.</summary>
	private const string ModelSourcesFileName = "config.json";

	/// <summary>
	///     Stock SimpleModelServer.config.json written on first run.
	///     Contains global settings, agent prompts, and model definitions in one file.
	/// </summary>
	private static readonly string StockJson = SerializeStockConfig();


	private static ServerConfig CreateStockConfig()
	{
		return new ServerConfig
		{
			Global = new GlobalSettings
			{
				DefaultAgentType = "ask",
				GpuLayers = "auto",
				CpuThreads = "auto",
				ConfigurationId = 1,
				PreferenceMode = PreferenceMode.Auto,
				OllamaPort = 11434,
				RouterPort = 11435,
				RouterHost = "localhost",
				RouterModelName = "HelperModel",
				TrustedNetworks = [],
				TrustedApiKeys = [],
				MdnsEnabled = true,
			},
			Models = new ModelDefinitions
			{
				Configurations = new Collection<ModelConfiguration>
				{
                    // Fast & Responsive: Small models for quick responses
                    new ModelConfiguration
					{
						Id = 1,
						Nickname = "Fast & Responsive",
						IsDefault = true,
						Description = "Small, fast models optimized for quick responses and low latency",
						Families = new Collection<FamilyPair>
						{
							new FamilyPair
							{
								Family = ModelFamily.Qwen25,
								Variant = "coder",
								KeepAliveMinutes = 5,
								Draft = new ModelConfig
								{
									Size = "0.5b",
									Temperature = 0.1,
									TopP = 0.9,
									NumCtx = 2048,
									Stop = ["```json", "{\"name\":", "\"arguments\":"],
								},
								Main = new ModelConfig
								{
									Size = "3b",
									Temperature = 0.1,
									TopP = 0.9,
									NumCtx = 4096,
									Stop = ["```json", "{\"name\":", "\"arguments\":", "{\"function\"", "\"tool_call"],
								},
							}
						},
						Embed = new EmbedConfig
						{
							Name = "nomic-embed-text",
							NumCtx = 8192,
							KeepAliveMinutes = 30,
							Stop = [],
						},
					},

                    // Accurate & Thorough: Larger models for higher quality
                    new ModelConfiguration
					{
						Id = 2,
						Nickname = "Accurate & Thorough",
						IsDefault = false,
						Description = "Larger models optimized for accuracy and quality, accepts higher latency",
						Families = new Collection<FamilyPair>
						{
							new FamilyPair
							{
								Family = ModelFamily.DeepSeek,
								Variant = "coder",
								KeepAliveMinutes = 10,
								Draft = new ModelConfig
								{
									Size = "1.3b",
									Temperature = 0.1,
									TopP = 0.9,
									NumCtx = 4096,
									Stop = ["```json", "{\"name\":", "\"arguments\":"],
								},
								Main = new ModelConfig
								{
									Size = "6.7b",
									Temperature = 0.1,
									TopP = 0.9,
									NumCtx = 8192,
									Stop = ["```json", "{\"name\":", "\"arguments\":", "{\"function\"", "\"tool_call"],
								},
							}
						},
						Embed = new EmbedConfig
						{
							Name = "nomic-embed-text",
							NumCtx = 8192,
							KeepAliveMinutes = 30,
							Stop = [],
						},
					}
				}
			},
			Agents = new AgentDefinitions(),
		};
	}

	/// <summary>
	///     Resolves the config path for SimpleModelServer.Chat.
	///     Uses the same ProgramData approach as LoaderConfigPath for consistency.
	///     Priority: SIMPLEMODELSERVER_WORKSPACE env var → %ProgramData%\SimpleModelServer → alongside the exe.
	/// </summary>
	public static string ChatConfigPath()
	{
		return ConfigFileManager.GetConfigFilePath(FileName);
	}

	/// <summary>
	///     Resolves the config path for SimpleModelServer.Loader.
	///     Priority: SIMPLEMODELSERVER_WORKSPACE env var → %ProgramData%\SimpleModelServer → alongside the exe.
	/// </summary>
	public static string LoaderConfigPath()
	{
		return ConfigFileManager.GetConfigFilePath(FileName);
	}

	/// <summary>
	///     Resolves the model sources (config.json) path alongside the primary config file.
	/// </summary>
	public static string ModelSourcesPath(string primaryConfigPath)
	{
		return Path.Combine(
			Path.GetDirectoryName(Path.GetFullPath(primaryConfigPath)) ?? AppContext.BaseDirectory,
			ModelSourcesFileName);
	}

	/// <summary>
	///     Writes the stock config to <paramref name="path" /> if it does not already exist.
	///     Creates any missing parent directories.
	/// </summary>
	public static void EnsureConfig(string path)
	{
		if (File.Exists(path))
		{
			return;
		}

		string? dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir))
		{
			_ = Directory.CreateDirectory(dir);
		}

		File.WriteAllText(path, StockJson);
	}

	/// <summary>
	///     No-op: model sources are now embedded in SimpleModelServer.config.json.
	///     Kept for call-site compatibility; callers can be cleaned up incrementally.
	/// </summary>
	public static void EnsureModelSources(string path)
	{
	}
}
