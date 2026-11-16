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
	private static partial void LogAdminInvalidConfigUpdate(ILogger logger, string remoteAddress, string setting, object? value, string? error);

	[LoggerMessage(Level = LogLevel.Warning, Message = "[ADMIN] Failed to update config from {RemoteAddress}: {Setting} = {Value} — {Error}")]
	private static partial void LogAdminFailedConfigUpdate(ILogger logger, string remoteAddress, string setting, object? value, string? error);

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

}
