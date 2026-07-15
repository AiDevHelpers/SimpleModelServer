using SimpleModelServer.Config.Configuration;
using Microsoft.AspNetCore.Http;
using SimpleModelServer.Server.Configuration;

using Microsoft.Extensions.Logging;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleModelServer.Server;

/// <summary>
///     Owns the GET/POST /admin/* endpoints (config read/update, hot reload, and the admin UI
///     page) — split out of <see cref="RouterHttpServer" /> so the transport-layer router class
///     isn't also carrying config-admin business logic. <see cref="RouterHttpServer" /> still
///     owns routing, auth-gate enforcement for non-admin routes, and response writing for
///     everything else; this class handles only what's under /admin.
/// </summary>
internal sealed partial class AdminEndpointHandler(
    IAppConfig config,
    ILogger logger,
    IConfigurationManager configurationManager,
    IConfigurationUpdater configurationUpdater,
    IAdminAuthenticationService adminAuthenticationService)
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "[ADMIN] Admin UI not found at {Path}")]
    private static partial void LogAdminUiNotFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "[ADMIN] Error serving admin UI")]
    private static partial void LogAdminUiError(ILogger logger, Exception ex);

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

    /// <summary>
    ///     Serves the admin UI HTML page.
    /// </summary>
    internal async Task ServeAdminUIAsync(HttpContext context)
    {
        try
        {
            string adminUiPath = Path.Combine(AppContext.BaseDirectory, "admin", "index.html");
            if (!File.Exists(adminUiPath))
            {
                LogAdminUiNotFound(logger, adminUiPath);
                await RouterHttpUtilities.WriteJsonAsync(context, 404, new { error = "Admin UI not available" });
                return;
            }

            string html = await File.ReadAllTextAsync(adminUiPath);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.StatusCode = 200;

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            context.Response.ContentLength = buffer.Length;
            await context.Response.Body.WriteAsync(buffer);
        }
        catch (Exception ex)
        {
            LogAdminUiError(logger, ex);
            await RouterHttpUtilities.WriteJsonAsync(context, 500, new { error = "Internal server error" });
        }
    }

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
        // In a more complex scenario, you might trigger model reloading, cache clearing, etc.

        await RouterHttpUtilities.WriteJsonAsync(context, 200, new
        {
            success = true,
            message = "Hot-reloadable configuration reloaded. Settings requiring restart must be applied via server restart.",
            note = "For settings marked requiresRestart, restart the server to apply changes."
        });
    }
}
