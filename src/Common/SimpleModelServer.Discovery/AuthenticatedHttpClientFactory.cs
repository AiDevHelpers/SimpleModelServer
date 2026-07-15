using System.Net.Http.Headers;

namespace SimpleModelServer.Discovery;

/// <summary>
/// Factory for creating HTTP clients configured with authentication headers based on a resolved server connection.
/// </summary>
public static class AuthenticatedHttpClientFactory
{
    /// <summary>
    /// Creates an HTTP client configured with the base address and authentication headers for the given connection.
    /// </summary>
    /// <param name="connection">The resolved server connection.</param>
    /// <returns>A configured HTTP client.</returns>
    public static HttpClient Create(ResolvedServerConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var client = new HttpClient
        {
            BaseAddress = new Uri(connection.BaseUrl),
        };

        // Only add Authorization header for non-loopback connections with an API key
        if (!connection.IsLoopback && !string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        }

        return client;
    }
}
