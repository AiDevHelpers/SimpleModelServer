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
