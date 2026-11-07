using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using Microsoft.AspNetCore.Http;
using SimpleModelServer.Server.Configuration;

using Microsoft.Extensions.Logging;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server;

/// <summary>
///     Owns the GET/POST /admin/* endpoints (config read/update, hot reload, MCP upstream
///     list/add/remove, and the admin UI page) — split out of <see cref="RouterHttpServer" /> so
///     the transport-layer router class isn't also carrying config-admin business logic.
///     <see cref="RouterHttpServer" /> still owns routing, auth-gate enforcement for non-admin
///     routes, and response writing for everything else; this class handles only what's under
///     /admin.
/// </summary>
internal sealed partial class AdminEndpointHandler(
	IAppConfig config,
	ILogger logger,
	IConfigurationManager configurationManager,
	IConfigurationUpdater configurationUpdater,
	IAdminAuthenticationService adminAuthenticationService,
	ModelReloadCoordinator reloadCoordinator,
	McpUpstreamRegistry mcpUpstreamRegistry)
{
	[LoggerMessage(Level = LogLevel.Warning, Message = "[ADMIN] [GET] /admin/config — unauthorized access attempt from {RemoteAddress}")]
	private static partial void LogAdminGetConfigUnauthorized(ILogger logger, string remoteAddress);

	[LoggerMessage(Level = LogLevel.Information, Message = "[ADMIN] [GET] /admin/config from {RemoteAddress}")]
	private static partial void LogAdminGetConfig(ILogger logger, string remoteAddress);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[ADMIN] [POST] /admin/config — unauthorized access attempt from {RemoteAddress}")]
	private static partial void LogAdminPostConfigUnauthorized(ILogger logger, string remoteAddress);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[ADMIN] Invalid config update from {RemoteAddress}: {Setting} = {Value} — {Error}")]
	private static partial void LogAdminInvalidConfigUpdate(ILogger logger, string remoteAddress, string setting, object? value, string error);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[ADMIN] Failed to update config from {RemoteAddress}: {Setting} = {Value} — {Error}")]
	private static partial void LogAdminFailedConfigUpdate(ILogger logger, string remoteAddress, string setting, object? value, string error);

	[LoggerMessage(Level = LogLevel.Information, Message = "[ADMIN] Configuration updated from {RemoteAddress}: {Setting} = {Value} (requiresRestart={RequiresRestart})")]
	private static partial void LogAdminConfigUpdated(ILogger logger, string remoteAddress, string setting, object? value, bool requiresRestart);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[ADMIN] Invalid JSON in config update from {RemoteAddress}: {Error}")]
	private static partial void LogAdminInvalidJson(ILogger logger, string remoteAddress, string error);

	[LoggerMessage(Level = LogLevel.Error, Message = "[ADMIN] Unexpected error in config update from {RemoteAddress}")]
	private static partial void LogAdminConfigUpdateError(ILogger logger, Exception ex, string remoteAddress);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[ADMIN] [POST] /admin/reload — unauthorized access attempt from {RemoteAddress}")]
	private static partial void LogAdminReloadUnauthorized(ILogger logger, string remoteAddress);

	[LoggerMessage(Level = LogLevel.Information, Message = "[ADMIN] [POST] /admin/reload from {RemoteAddress}")]
	private static partial void LogAdminReload(ILogger logger, string remoteAddress);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[ADMIN] /admin/mcp-servers — unauthorized access attempt from {RemoteAddress}")]
	private static partial void LogAdminMcpServersUnauthorized(ILogger logger, string remoteAddress);

	[LoggerMessage(Level = LogLevel.Information, Message = "[ADMIN] MCP server '{ServerName}' added/updated from {RemoteAddress}")]
	private static partial void LogAdminMcpServerAdded(ILogger logger, string serverName, string remoteAddress);

	[LoggerMessage(Level = LogLevel.Information, Message = "[ADMIN] MCP server '{ServerName}' removed from {RemoteAddress}")]
	private static partial void LogAdminMcpServerRemoved(ILogger logger, string serverName, string remoteAddress);

	/// <summary>
	///     GET /admin/config — Retrieves the current configuration as JSON.
	///     Requires admin authentication (localhost or valid API key).
	/// </summary>
	internal async Task HandleGetConfigAsync(HttpContext context)
	{
		if (!adminAuthenticationService.IsAuthorized(context.Request))
		{
			LogAdminGetConfigUnauthorized(logger, RouterHttpUtilities.RemoteAddress(context));
			await RouterHttpUtilities.WriteJsonAsync(context, 401, new
			{
				error = "unauthorized",
				message = "Admin configuration requires authentication (localhost trusted by default, remote requests require Bearer token)"
			});
			return;
		}

		LogAdminGetConfig(logger, RouterHttpUtilities.RemoteAddress(context));

		var configData = new
		{
			global = new
			{
				routerPort = config.Global.RouterPort,
				ollamaPort = config.Global.OllamaPort,
				routerHost = config.Global.RouterHost,
				routerModelName = config.Global.RouterModelName,
				draftModel = config.Global.DraftModel,
				mainModel = config.Global.MainModel,
				configurationId = config.Global.ConfigurationId,
				embedModel = config.Global.EmbedModel,
				contextSize = config.Global.ContextSize,
				defaultAgentType = config.Global.DefaultAgentType,
				agentType = config.Global.AgentType,
				draftHandoffTimeoutMs = config.Global.DraftHandoffTimeoutMs,
				draftMode = config.Global.DraftMode.ToString(),
				trustedNetworks = config.Global.TrustedNetworks,
				trustedApiKeys = Array.Empty<string>(), // Never return actual keys for security
				mdnsEnabled = config.Global.MdnsEnabled,
				mdnsServiceName = config.Global.MdnsServiceName,
				gpuLayers = config.Global.GpuLayers.IsSpecified ?
					(config.Global.GpuLayers.Auto ? "auto" : config.Global.GpuLayers.Value?.ToString() ?? "unspecified")
					: "unspecified",
				cpuThreads = config.Global.CpuThreads.IsSpecified ?
					(config.Global.CpuThreads.Auto ? "auto" : config.Global.CpuThreads.Value?.ToString() ?? "unspecified")
					: "unspecified"
			},
			agents = config.Agents.ToDictionary(
				kvp => kvp.Key,
				kvp => new
				{
					systemPrompt = kvp.Value.SystemPrompt,
					temperature = kvp.Value.Temperature
				}),
			modelCount = config.ModelSources.Count,
			profiles = config.ModelProfiles.Select(p => new { id = p.Id, nickname = p.Nickname, isDefault = p.IsDefault }),
			note = "Localhost is always trusted. Remote requests require 'Authorization: Bearer <key>' header."
		};

		await RouterHttpUtilities.WriteJsonAsync(context, 200, configData);
	}

	/// <summary>
	///     POST /admin/config — Updates a configuration setting.
	///     Body: { "setting": "global.routerPort", "value": 11436 }
	///     Returns: { "success": true, "requiresRestart": true, "message": "..." }
	/// </summary>
	internal async Task HandleUpdateConfigAsync(HttpContext context)
	{
		if (!adminAuthenticationService.IsAuthorized(context.Request))
		{
			LogAdminPostConfigUnauthorized(logger, RouterHttpUtilities.RemoteAddress(context));
			await RouterHttpUtilities.WriteJsonAsync(context, 401, new
			{
				error = "unauthorized",
				message = "Admin configuration requires authentication (localhost trusted by default, remote requests require Bearer token)"
			});
			return;
		}

		try
		{
			string bodyText = await RouterHttpUtilities.ReadBodyAsync(context.Request, config, logger);
			JsonNode? requestBody = JsonNode.Parse(bodyText);
			if (requestBody is not JsonObject bodyObj)
			{
				await RouterHttpUtilities.WriteJsonAsync(context, 400, new { error = "Invalid request body. Expected JSON object." });
				return;
			}

			string? settingPath = bodyObj["setting"]?.GetValue<string>();
			object? value = bodyObj["value"];

			if (string.IsNullOrWhiteSpace(settingPath))
			{
				await RouterHttpUtilities.WriteJsonAsync(context, 400, new { error = "Missing or empty 'setting' field" });
				return;
			}

			if (settingPath.StartsWith("agents.", StringComparison.OrdinalIgnoreCase))
			{
				await HandleUpdateAgentSettingAsync(context, settingPath, value);
				return;
			}

			if (IsConfigurationIdSetting(settingPath) && !IsKnownProfile(config, value))
			{
				string valid = string.Join(", ", config.ModelProfiles.Select(p => p.Id));
				await RouterHttpUtilities.WriteJsonAsync(context, 400, new
				{
					error = "unknown_profile",
					message = $"No model profile with id '{value}'. Valid ids: {(valid.Length == 0 ? "none (flat models schema)" : valid)}."
				});
				return;
			}

			// Validate the setting
			ValidationResult validation = configurationManager.Validate(settingPath, value);
			if (!validation.IsValid)
			{
				LogAdminInvalidConfigUpdate(logger, RouterHttpUtilities.RemoteAddress(context), settingPath, value, validation.ErrorMessage);
				await RouterHttpUtilities.WriteJsonAsync(context, 400, new
				{
					error = "validation_failed",
					message = validation.ErrorMessage
				});
				return;
			}

			// Apply the update
			string settingName = settingPath.Contains('.', StringComparison.Ordinal) ? settingPath.Split('.')[^1] : settingPath;
			UpdateResult updateResult = configurationUpdater.UpdateGlobalSetting(config.Global, settingName, value);

			if (!updateResult.Success)
			{
				LogAdminFailedConfigUpdate(logger, RouterHttpUtilities.RemoteAddress(context), settingPath, value, updateResult.ErrorMessage);
				await RouterHttpUtilities.WriteJsonAsync(context, 400, new
				{
					error = "update_failed",
					message = updateResult.ErrorMessage
				});
				return;
			}

			LogAdminConfigUpdated(logger, RouterHttpUtilities.RemoteAddress(context), settingPath, value, updateResult.RequiresRestart);

			await RouterHttpUtilities.WriteJsonAsync(context, 200, new
			{
				success = true,
				setting = settingPath,
				requiresRestart = updateResult.RequiresRestart,
				message = updateResult.RequiresRestart
					? $"Setting '{settingPath}' updated. Server restart required for change to take effect."
					: $"Setting '{settingPath}' updated and reloaded."
			});
		}
		catch (JsonException ex)
		{
			LogAdminInvalidJson(logger, RouterHttpUtilities.RemoteAddress(context), ex.Message);
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new
			{
				error = "invalid_json",
				message = "Request body must be valid JSON"
			});
		}
		catch (Exception ex)
		{
			LogAdminConfigUpdateError(logger, ex, RouterHttpUtilities.RemoteAddress(context));
			await RouterHttpUtilities.WriteJsonAsync(context, 500, new { error = "internal_server_error" });
		}
	}

	/// <summary>
	///     Handles "agents.&lt;agentName&gt;.&lt;field&gt;" setting paths from
	///     <see cref="HandleUpdateConfigAsync" />. When the changed field is the agent's
	///     systemPrompt, the response flags <c>suggestGoldenSet</c> so callers (e.g. the VS
	///     extension's settings UI) can prompt the developer to re-run the golden-set eval before
	///     trusting the new prompt's behavior.
	/// </summary>
	/// <summary>True when the setting path targets <c>global.configurationId</c> (camelCase or snake_case).</summary>
	private static bool IsConfigurationIdSetting(string settingPath)
	{
		string leaf = settingPath.Contains('.', StringComparison.Ordinal)
			? settingPath[(settingPath.LastIndexOf('.') + 1)..]
			: settingPath;
		return string.Equals(leaf, "configurationId", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(leaf, "configuration_id", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>True when <paramref name="value" /> names one of the configured model profiles.</summary>
	private static bool IsKnownProfile(IAppConfig config, object? value)
	{
		return int.TryParse(value?.ToString(), out int id) && config.ModelProfiles.Any(p => p.Id == id);
	}

	private async Task HandleUpdateAgentSettingAsync(HttpContext context, string settingPath, object? value)
	{
		string[] parts = settingPath.Split('.');
		if (parts.Length != 3)
		{
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new
			{
				error = "invalid_setting_path",
				message = "Agent settings must use the form 'agents.<agentName>.<field>', e.g. 'agents.code.systemPrompt'"
			});
			return;
		}

		string agentName = parts[1];
		string fieldName = parts[2];

		ValidationResult validation = configurationManager.ValidateAgentField(fieldName, value);
		if (!validation.IsValid)
		{
			LogAdminInvalidConfigUpdate(logger, RouterHttpUtilities.RemoteAddress(context), settingPath, value, validation.ErrorMessage);
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new
			{
				error = "validation_failed",
				message = validation.ErrorMessage
			});
			return;
		}

		UpdateResult updateResult = configurationUpdater.UpdateAgentSetting(config.Agents, agentName, fieldName, value);
		if (!updateResult.Success)
		{
			LogAdminFailedConfigUpdate(logger, RouterHttpUtilities.RemoteAddress(context), settingPath, value, updateResult.ErrorMessage);
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new
			{
				error = "update_failed",
				message = updateResult.ErrorMessage
			});
			return;
		}

		LogAdminConfigUpdated(logger, RouterHttpUtilities.RemoteAddress(context), settingPath, value, updateResult.RequiresRestart);

		bool suggestGoldenSet = string.Equals(fieldName, "systemPrompt", StringComparison.OrdinalIgnoreCase);

		await RouterHttpUtilities.WriteJsonAsync(context, 200, new
		{
			success = true,
			setting = settingPath,
			requiresRestart = false,
			suggestGoldenSet,
			message = suggestGoldenSet
				? $"Agent '{agentName}' prompt updated and reloaded. Consider running the golden set (SimpleModelServer.Evals --mode router) to validate the change."
				: $"Agent '{agentName}' setting '{fieldName}' updated and reloaded."
		});
	}

	/// <summary>
	///     POST /admin/reload — Reloads configuration changes that don't require restart.
	///     For settings requiring restart, this does nothing (restart required via other means).
	/// </summary>
	internal async Task HandleReloadAsync(HttpContext context)
	{
		if (!adminAuthenticationService.IsAuthorized(context.Request))
		{
			LogAdminReloadUnauthorized(logger, RouterHttpUtilities.RemoteAddress(context));
			await RouterHttpUtilities.WriteJsonAsync(context, 401, new
			{
				error = "unauthorized",
				message = "Admin reload requires authentication"
			});
			return;
		}

		LogAdminReload(logger, RouterHttpUtilities.RemoteAddress(context));

		// Reload settings that can be hot-reloaded (trusted networks, API keys, etc.)
		// These are already applied to config.Global, so no extra work needed here.

		// Optional { "model": "<name>" } body triggers an actual model swap via
		// ModelReloadCoordinator (pause subscribed clients, unload/reload, resume) — absent body
		// just reloads hot-reloadable config, same as before this parameter existed.
		string body = await RouterHttpUtilities.ReadBodyAsync(context.Request, config, logger);
		string? modelName = null;
		if (!string.IsNullOrWhiteSpace(body))
		{
			try
			{
				modelName = (JsonNode.Parse(body) as JsonObject)?["model"]?.GetValue<string>();
			}
			catch (JsonException)
			{
				// No/invalid body — treat as a plain config-only reload, same as an empty POST.
			}
		}

		if (!string.IsNullOrWhiteSpace(modelName))
		{
			await reloadCoordinator.BeginReloadAsync(modelName, context.RequestAborted);
			await RouterHttpUtilities.WriteJsonAsync(context, 200, new
			{
				success = true,
				message = $"Model '{modelName}' reloaded.",
				model = modelName
			});
			return;
		}

		await RouterHttpUtilities.WriteJsonAsync(context, 200, new
		{
			success = true,
			message = "Hot-reloadable configuration reloaded. Settings requiring restart must be applied via server restart.",
			note = "For settings marked requiresRestart, restart the server to apply changes."
		});
	}

	/// <summary>
	///     GET /admin/mcp-servers — lists every configured MCP upstream, name plus its live
	///     connection status from <see cref="McpUpstreamRegistry.GetStatus" />.
	/// </summary>
	internal async Task HandleListMcpServersAsync(HttpContext context)
	{
		if (!adminAuthenticationService.IsAuthorized(context.Request))
		{
			LogAdminMcpServersUnauthorized(logger, RouterHttpUtilities.RemoteAddress(context));
			await RouterHttpUtilities.WriteJsonAsync(context, 401, new
			{
				error = "unauthorized",
				message = "Admin configuration requires authentication (localhost trusted by default, remote requests require Bearer token)"
			});
			return;
		}

		var mcpServers = mcpUpstreamRegistry.GetStatus().ToDictionary(
			kvp => kvp.Key,
			kvp => new
			{
				transportType = kvp.Value.TransportType.ToString(),
				path = kvp.Value.Path,
				connected = kvp.Value.Connected,
				toolCount = kvp.Value.ToolCount
			});

		await RouterHttpUtilities.WriteJsonAsync(context, 200, new { mcpServers });
	}

	/// <summary>
	///     POST /admin/mcp-servers — adds a new MCP upstream, or replaces an existing one with the
	///     same name. Body: <c>{ "name": "...", "transportType": "Http"|"Stdio", "path": "...", "args": ["..."] }</c>.
	///     <c>transportType</c> defaults to "Http" when omitted. Connects the upstream immediately
	///     (see <see cref="McpUpstreamRegistry.ConnectAsync" />) — no restart required for HTTP
	///     upstreams to become usable through the /mcp gateway.
	/// </summary>
	internal async Task HandleAddMcpServerAsync(HttpContext context)
	{
		if (!adminAuthenticationService.IsAuthorized(context.Request))
		{
			LogAdminMcpServersUnauthorized(logger, RouterHttpUtilities.RemoteAddress(context));
			await RouterHttpUtilities.WriteJsonAsync(context, 401, new
			{
				error = "unauthorized",
				message = "Admin configuration requires authentication (localhost trusted by default, remote requests require Bearer token)"
			});
			return;
		}

		JsonObject bodyObj;
		try
		{
			string bodyText = await RouterHttpUtilities.ReadBodyAsync(context.Request, config, logger);
			if (JsonNode.Parse(bodyText) is not JsonObject parsed)
			{
				await RouterHttpUtilities.WriteJsonAsync(context, 400, new { error = "Invalid request body. Expected JSON object." });
				return;
			}

			bodyObj = parsed;
		}
		catch (JsonException ex)
		{
			LogAdminInvalidJson(logger, RouterHttpUtilities.RemoteAddress(context), ex.Message);
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new { error = "invalid_json", message = "Request body must be valid JSON" });
			return;
		}

		string? name = bodyObj["name"]?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(name))
		{
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new { error = "validation_failed", message = "Missing or empty 'name' field" });
			return;
		}

		if (name.Contains('.', StringComparison.Ordinal))
		{
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new
			{
				error = "validation_failed",
				message = "'name' cannot contain '.' — the gateway namespaces tools as '<name>.<toolName>'"
			});
			return;
		}

		string? path = bodyObj["path"]?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(path))
		{
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new { error = "validation_failed", message = "Missing or empty 'path' field" });
			return;
		}

		string? transportTypeRaw = bodyObj["transportType"]?.GetValue<string>();
		if (!TryParseTransportType(transportTypeRaw, out McpTransportType transportType))
		{
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new
			{
				error = "validation_failed",
				message = $"'transportType' must be one of: {string.Join(", ", Enum.GetNames<McpTransportType>())}"
			});
			return;
		}

		List<string> args = [];
		if (bodyObj["args"] is JsonArray argsArray)
		{
			foreach (JsonNode? item in argsArray)
			{
				string? arg = item?.GetValue<string>();
				if (!string.IsNullOrEmpty(arg))
				{
					args.Add(arg);
				}
			}
		}
		else if (bodyObj["args"] is not null)
		{
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new { error = "validation_failed", message = "'args' must be an array of strings" });
			return;
		}

		IMcpUpstreamConfig serverConfig = new McpUpstreamConfig
		{
			TransportType = transportType,
			Path = path,
			Args = args
		};

		config.AddOrUpdateMcpServer(name, serverConfig);
		await mcpUpstreamRegistry.DisconnectAsync(name);
		await mcpUpstreamRegistry.ConnectAsync(name, serverConfig, context.RequestAborted);

		LogAdminMcpServerAdded(logger, name, RouterHttpUtilities.RemoteAddress(context));

		await RouterHttpUtilities.WriteJsonAsync(context, 200, new
		{
			success = true,
			name,
			message = $"MCP server '{name}' added."
		});
	}

	/// <summary>
	///     POST /admin/mcp-servers/remove — removes a configured MCP upstream and disconnects it
	///     from the live gateway. Body: <c>{ "name": "..." }</c>.
	/// </summary>
	internal async Task HandleRemoveMcpServerAsync(HttpContext context)
	{
		if (!adminAuthenticationService.IsAuthorized(context.Request))
		{
			LogAdminMcpServersUnauthorized(logger, RouterHttpUtilities.RemoteAddress(context));
			await RouterHttpUtilities.WriteJsonAsync(context, 401, new
			{
				error = "unauthorized",
				message = "Admin configuration requires authentication (localhost trusted by default, remote requests require Bearer token)"
			});
			return;
		}

		string? name;
		try
		{
			string bodyText = await RouterHttpUtilities.ReadBodyAsync(context.Request, config, logger);
			if (JsonNode.Parse(bodyText) is not JsonObject bodyObj)
			{
				await RouterHttpUtilities.WriteJsonAsync(context, 400, new { error = "Invalid request body. Expected JSON object." });
				return;
			}

			name = bodyObj["name"]?.GetValue<string>();
		}
		catch (JsonException ex)
		{
			LogAdminInvalidJson(logger, RouterHttpUtilities.RemoteAddress(context), ex.Message);
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new { error = "invalid_json", message = "Request body must be valid JSON" });
			return;
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			await RouterHttpUtilities.WriteJsonAsync(context, 400, new { error = "validation_failed", message = "Missing or empty 'name' field" });
			return;
		}

		if (!config.RemoveMcpServer(name))
		{
			await RouterHttpUtilities.WriteJsonAsync(context, 404, new { error = "not_found", message = $"No MCP server named '{name}' is configured." });
			return;
		}

		await mcpUpstreamRegistry.DisconnectAsync(name);

		LogAdminMcpServerRemoved(logger, name, RouterHttpUtilities.RemoteAddress(context));

		await RouterHttpUtilities.WriteJsonAsync(context, 200, new
		{
			success = true,
			name,
			message = $"MCP server '{name}' removed."
		});
	}

	private static bool TryParseTransportType(string? value, out McpTransportType transportType)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			transportType = McpTransportType.Http;
			return true;
		}

		foreach (McpTransportType candidate in Enum.GetValues<McpTransportType>())
		{
			if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
			{
				transportType = candidate;
				return true;
			}
		}

		transportType = default;
		return false;
	}
}
