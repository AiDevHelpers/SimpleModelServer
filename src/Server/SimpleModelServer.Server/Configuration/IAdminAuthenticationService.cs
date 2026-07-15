using Microsoft.AspNetCore.Http;

namespace SimpleModelServer.Server.Configuration;

/// <summary>
///     Handles authentication for admin configuration endpoints.
/// </summary>
internal interface IAdminAuthenticationService
{
    /// <summary>
    ///     Checks if a request is authorized for admin configuration endpoints.
    ///     Returns true if localhost or valid API key is provided.
    /// </summary>
    bool IsAuthorized(HttpRequest request);
}
