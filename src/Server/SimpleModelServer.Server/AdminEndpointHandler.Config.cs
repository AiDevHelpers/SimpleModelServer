using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using Microsoft.AspNetCore.Http;
using SimpleModelServer.Server.Configuration;

using Microsoft.Extensions.Logging;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server;

internal sealed partial class AdminEndpointHandler
{
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
			profiles = config.ModelProfiles.Select(p => new { id = p.Id, nickname = p.Nickname, isDefault = p.IsDefault, mainModel = p.MainModel }),
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

}
