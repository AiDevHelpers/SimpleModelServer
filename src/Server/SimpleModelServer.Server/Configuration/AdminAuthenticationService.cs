using Microsoft.AspNetCore.Http;
using SimpleModelServer.Config.Configuration;
using System.Net;

namespace SimpleModelServer.Server.Configuration;

/// <summary>
///     Handles authentication for admin configuration endpoints.
///     Trusts localhost by default and validates API keys for remote requests.
/// </summary>
internal sealed class AdminAuthenticationService : IAdminAuthenticationService
{
	private readonly IAppConfig config;

	public AdminAuthenticationService(IAppConfig config)
	{
		this.config = config ?? throw new ArgumentNullException(nameof(config));
	}

	/// <summary>
	///     Checks if a request is authorized for admin configuration endpoints.
	///     Returns true if localhost or valid API key is provided.
	/// </summary>
	public bool IsAuthorized(HttpRequest request)
	{
		IPAddress? remoteAddress = request.HttpContext.Connection.RemoteIpAddress;

		// Trust localhost by default
		if (remoteAddress is not null &&
			(IPAddress.IsLoopback(remoteAddress) ||
			 remoteAddress.Equals(IPAddress.IPv6Loopback)))
		{
			return true;
		}

		// For remote requests, require a valid API key
		string? apiKey = ExtractBearerToken(request);
		return IsValidApiKey(apiKey);
	}

	/// <summary>
	///     Extracts the Bearer token from the Authorization header.
	/// </summary>
	private static string? ExtractBearerToken(HttpRequest request)
	{
		string? authHeader = request.Headers["Authorization"];
		if (string.IsNullOrWhiteSpace(authHeader))
		{
			return null;
		}

		const string bearerScheme = "Bearer ";
		if (authHeader.StartsWith(bearerScheme, StringComparison.OrdinalIgnoreCase))
		{
			return authHeader[bearerScheme.Length..].Trim();
		}

		return null;
	}

	/// <summary>
	///     Checks if the provided API key is valid.
	/// </summary>
	private bool IsValidApiKey(string? apiKey)
	{
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			return false;
		}

		return config.Global.TrustedApiKeys.Contains(apiKey, StringComparer.Ordinal);
	}
}
