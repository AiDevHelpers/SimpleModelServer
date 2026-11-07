using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;

namespace SimpleModelServer.Config.Configuration;

public sealed partial class AppConfig
{
	/// <summary>
	///     Lists the structured <c>models.configurations</c> entries as selectable profiles. Empty for
	///     the flat schema, which has only one resolved set of models and nothing to switch between.
	/// </summary>
	private static IReadOnlyList<ModelProfile> ParseModelProfiles(JsonObject modelsNode)
	{
		if (GetNode(modelsNode, "configurations") is not JsonArray configurations)
		{
			return [];
		}

		// Each profile's main model is the "main" role that ParseStructuredModels resolves for its id.
		// Profiles with no main role get a null MainModel.
		return configurations
			.OfType<JsonObject>()
			.Where(c => (GetInt(c, "id") ?? 0) > 0)
			.Select(c =>
			{
				int id = GetInt(c, "id") ?? 0;
				string? mainModel = ParseStructuredModels(configurations, id).roles.TryGetValue("main", out string? name)
					? name
					: null;
				return new ModelProfile(
					id,
					GetString(c, "nickname") ?? "",
					GetBool(c, "isDefault", "is_default") == true,
					mainModel);
			})
			.ToList();
	}

	/// <summary>
	///     Parse an inline "models" JsonObject. Two shapes are accepted:
	///     <list type="bullet">
	///       <item>Structured (what <c>DefaultConfig</c> writes): <c>{"configurations":[...]}</c> —
	///         see <see cref="ParseStructuredModels" />.</item>
	///       <item>Flat (legacy): role-keyed objects, e.g. <c>{"draft":{"name":...,"base":...}}</c>.</item>
	///     </list>
	/// </summary>
	private static (Dictionary<string, IModelSourceConfig> sources, Dictionary<string, string> roles) ParseModelSources(
		JsonObject modelsNode, int? configurationId = null)
	{
		if (GetNode(modelsNode, "configurations") is JsonArray configurations)
		{
			return ParseStructuredModels(configurations, configurationId);
		}

		Dictionary<string, IModelSourceConfig> sources = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> roles = new(StringComparer.OrdinalIgnoreCase);

		foreach (KeyValuePair<string, JsonNode?> pair in modelsNode)
		{
			if (pair.Value is not JsonObject modelObj)
			{
				continue;
			}

			// Try to get explicit name first, fall back to auto-generated from family/variant/size
			string? explicitName = GetString(modelObj, "name");
			string? family = GetString(modelObj, "family");
			string? variant = GetString(modelObj, "variant");
			string? size = GetString(modelObj, "size");
			string? chatSuffix = GetString(modelObj, "chatSuffix", "chat_suffix") ?? "-chat";

			string name = explicitName ?? GenerateModelName(family, variant, size, chatSuffix);
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			// Try to get explicit base first, fall back to auto-generated from family/variant/size
			string? explicitBase = GetString(modelObj, "base");
			string base_ = explicitBase ?? GenerateModelBase(family, variant, size);

			sources[name] = new ModelSourceConfig
			{
				Family = family,
				Variant = variant,
				Size = size,
				ChatSuffix = chatSuffix,
				Base = base_,
				NumCtx = GetInt(modelObj, "num_ctx", "numCtx"),
				KeepAlive = GetString(modelObj, "keep_alive", "keepAlive"),
				// Explicit pin: when set, GpuBudgetPlanner leaves this model out of auto VRAM
				// planning entirely and this value is sent as-is on every generate/warmup call.
				NumGpu = GetInt(modelObj, "num_gpu", "numGpu"),
				Stop = ParseStopTokens(GetNode(modelObj, "stop")),
				Temperature = GetDouble(modelObj, "temperature"),
				TopP = GetDouble(modelObj, "topP", "top_p"),
			};

			roles[pair.Key] = name;
		}

		return (sources, roles);
	}

	/// <summary>
	///     Legacy: load models from a separate file (config.json). Used when the primary
	///     config has no inline "models" section.
	/// </summary>
	private static (Dictionary<string, IModelSourceConfig> sources, Dictionary<string, string> roles)
		LoadModelSourcesFromFile(string primaryConfigPath, string modelSourcesPath, int? configurationId)
	{
		(Dictionary<string, IModelSourceConfig>, Dictionary<string, string>) empty = (new Dictionary<string, IModelSourceConfig>(StringComparer.OrdinalIgnoreCase),
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

		if (string.IsNullOrWhiteSpace(modelSourcesPath))
		{
			return empty;
		}

		string baseDir = Path.GetDirectoryName(Path.GetFullPath(primaryConfigPath)) ?? Directory.GetCurrentDirectory();
		string fullPath = Path.IsPathRooted(modelSourcesPath)
			? modelSourcesPath
			: Path.Combine(baseDir, modelSourcesPath);

		if (!File.Exists(fullPath))
		{
			return empty;
		}

		try
		{
			JsonObject? json = JsonNode.Parse(File.ReadAllText(fullPath))?.AsObject();
			JsonObject? modelsNode = json is not null ? GetObject(json, "models") : null;
			return modelsNode is not null ? ParseModelSources(modelsNode, configurationId) : empty;
		}
		catch
		{
			return empty;
		}
	}

	/// <summary>
	///     Generates a model name from family, variant, and size components.
	///     Format: "{family}-{variant}-{size}{chatSuffix}" (e.g., "qwen2.5-coder-0.5b-chat").
	///     Returns empty string if any component is missing/empty.
	/// </summary>
	private static string GenerateModelName(string? family, string? variant, string? size, string? chatSuffix)
	{
		if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(variant) || string.IsNullOrWhiteSpace(size))
		{
			return "";
		}

		return $"{family}-{variant}-{size}{chatSuffix}";
	}

	/// <summary>
	///     Generates the Ollama registry reference to pull/create from, given family, variant, and size.
	///     Format: "{family}-{variant}:{size}" (e.g., "qwen2.5-coder:0.5b"). The chat suffix is
	///     deliberately NOT part of the base: it only names the derived local model, and the registry
	///     has no "-chat" tags. Returns empty string if any component is missing/empty.
	/// </summary>
	private static string GenerateModelBase(string? family, string? variant, string? size)
	{
		if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(variant) || string.IsNullOrWhiteSpace(size))
		{
			return "";
		}

		return $"{family}-{variant}:{size}";
	}

	private const string DefaultEmbedModelName = "nomic-embed-text";
	private const string DefaultDraftSize = "0.5b";
	private const int DefaultDraftNumCtx = 2048;
	private const string DefaultMainSize = "3b";
	private const int DefaultMainNumCtx = 4096;

	/// <summary>Stop tokens for code/instruction models when a model entry doesn't specify its own.</summary>
	private static readonly string[] DefaultStopTokens =
	[
		"```json", "{\"name\":", "\"arguments\":", "{\"function\":", "\"tool_call",
	];

	/// <summary>
	///     Resolves the structured <c>models.configurations</c> schema to role-keyed model sources.
	///     Picks one configuration (<paramref name="configurationId" /> if it matches, else the one
	///     flagged <c>isDefault</c>, else the first). Within it, the first family pair fills the
	///     <c>draft</c> and <c>main</c> roles (a missing draft/main entry is left out, except that
	///     when neither is given the default 0.5b/3b sizes are used), and <c>embed</c> comes from the
	///     configuration's embed entry.
	/// </summary>
	private static (Dictionary<string, IModelSourceConfig> sources, Dictionary<string, string> roles) ParseStructuredModels(
		JsonArray configurations, int? configurationId)
	{
		Dictionary<string, IModelSourceConfig> sources = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> roles = new(StringComparer.OrdinalIgnoreCase);

		JsonObject? chosen = SelectConfiguration(configurations, configurationId);
		if (chosen is null)
		{
			return (sources, roles);
		}

		if (GetNode(chosen, "families") is JsonArray families && families.FirstOrDefault(n => n is JsonObject) is JsonObject pair)
		{
			string? familyName = ToOllamaFamilyName(ParseModelFamily(GetNode(pair, "family")));
			string variant = GetString(pair, "variant") ?? "";
			int? pairKeepAlive = GetInt(pair, "keepAliveMinutes", "keep_alive_minutes");
			JsonObject? draft = GetObject(pair, "draft");
			JsonObject? main = GetObject(pair, "main");
			bool neitherSpecified = draft is null && main is null;

			if (draft is not null || neitherSpecified)
			{
				AddStructuredRole(sources, roles, "draft", draft ?? new JsonObject(), familyName, variant,
					pairKeepAlive, DefaultDraftSize, DefaultDraftNumCtx);
			}

			if (main is not null || neitherSpecified)
			{
				AddStructuredRole(sources, roles, "main", main ?? new JsonObject(), familyName, variant,
					pairKeepAlive, DefaultMainSize, DefaultMainNumCtx);
			}
		}

		JsonObject embed = GetObject(chosen, "embed") ?? new JsonObject();
		string embedName = GetString(embed, "name") is { Length: > 0 } explicitEmbed ? explicitEmbed : DefaultEmbedModelName;
		sources[embedName] = new ModelSourceConfig
		{
			Base = GetString(embed, "base") ?? embedName,
			NumCtx = GetInt(embed, "numCtx", "num_ctx") ?? 8192,
			KeepAlive = ToKeepAliveDuration(GetInt(embed, "keepAliveMinutes", "keep_alive_minutes")),
			Stop = ParseStopTokens(GetNode(embed, "stop")),
		};
		roles["embed"] = embedName;

		return (sources, roles);
	}

	private static JsonObject? SelectConfiguration(JsonArray configurations, int? configurationId)
	{
		List<JsonObject> candidates = configurations.OfType<JsonObject>().ToList();
		if (configurationId is int id && candidates.FirstOrDefault(c => GetInt(c, "id") == id) is { } byId)
		{
			return byId;
		}

		return candidates.FirstOrDefault(c => GetBool(c, "isDefault", "is_default") == true) ?? candidates.FirstOrDefault();
	}

	/// <summary>
	///     Adds one draft/main role from a structured model entry. Name, base, and the derived local
	///     model name follow the same rules as the flat schema; the family pair's keep-alive applies
	///     unless the entry sets its own. Entries that can't be resolved to a registry base (e.g.
	///     a custom family with no explicit name/base) are skipped rather than guessed at.
	/// </summary>
	private static void AddStructuredRole(
		Dictionary<string, IModelSourceConfig> sources,
		Dictionary<string, string> roles,
		string role,
		JsonObject modelNode,
		string? familyName,
		string variant,
		int? pairKeepAlive,
		string defaultSize,
		int defaultNumCtx)
	{
		if (roles.ContainsKey(role))
		{
			return;
		}

		string size = GetString(modelNode, "size") ?? defaultSize;
		string chatSuffix = GetString(modelNode, "chatSuffix", "chat_suffix") ?? "-chat";

		string name = GetString(modelNode, "name") is { Length: > 0 } explicitName
			? explicitName
			: GenerateModelName(familyName, variant, size, chatSuffix);
		string baseRef = GetString(modelNode, "base") is { Length: > 0 } explicitBase
			? explicitBase
			: GenerateModelBase(familyName, variant, size);

		if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseRef))
		{
			return;
		}

		int? keepAlive = GetInt(modelNode, "keepAliveMinutes", "keep_alive_minutes") ?? pairKeepAlive;

		sources[name] = new ModelSourceConfig
		{
			Family = familyName,
			Variant = variant,
			Size = size,
			ChatSuffix = chatSuffix,
			Base = baseRef,
			NumCtx = GetInt(modelNode, "numCtx", "num_ctx") ?? defaultNumCtx,
			KeepAlive = ToKeepAliveDuration(keepAlive),
			Stop = GetNode(modelNode, "stop") is null ? DefaultStopTokens : ParseStopTokens(GetNode(modelNode, "stop")),
			Temperature = GetDouble(modelNode, "temperature"),
			TopP = GetDouble(modelNode, "topP", "top_p"),
		};
		roles[role] = name;
	}

	/// <summary>
	///     Reads a <see cref="ModelFamily" /> from a structured entry. The serializer writes enums as
	///     integers by default; names are accepted too.
	/// </summary>
	private static ModelFamily? ParseModelFamily(JsonNode? node)
	{
		if (node is not JsonValue value)
		{
			return null;
		}

		if (value.TryGetValue(out int ordinal) && Enum.IsDefined(typeof(ModelFamily), ordinal))
		{
			return (ModelFamily)ordinal;
		}

		return value.TryGetValue(out string? text) && Enum.TryParse(text, ignoreCase: true, out ModelFamily parsed)
			? parsed
			: null;
	}

	/// <summary>
	///     Maps a family to the name Ollama's registry uses. <see cref="ModelFamily.Custom" /> has no
	///     registry name, so it returns null and such entries need an explicit name/base.
	/// </summary>
	private static string? ToOllamaFamilyName(ModelFamily? family) => family switch
	{
		ModelFamily.Qwen25 => "qwen2.5",
		ModelFamily.DeepSeek => "deepseek",
		ModelFamily.Llama2 => "llama2",
		ModelFamily.Mistral => "mistral",
		ModelFamily.Phi => "phi",
		ModelFamily.NeuralChat => "neural-chat",
		_ => null,
	};

	/// <summary>
	///     Converts the structured keep-alive minutes to Ollama's keep_alive duration. 0 means
	///     "always hot" (per the schema docs), which Ollama expresses as -1; null leaves Ollama's default.
	/// </summary>
	private static string? ToKeepAliveDuration(int? minutes) => minutes switch
	{
		null => null,
		0 => "-1",
		_ => $"{minutes.Value}m",
	};

	private static string[] ParseStopTokens(JsonNode? node)
	{
		if (node is not JsonArray arr)
		{
			return [];
		}

		List<string> result = [];
		foreach (JsonNode? item in arr)
		{
			string? s = item?.GetValue<string>();
			if (!string.IsNullOrEmpty(s))
			{
				result.Add(s);
			}
		}

		return result.ToArray();
	}

}
