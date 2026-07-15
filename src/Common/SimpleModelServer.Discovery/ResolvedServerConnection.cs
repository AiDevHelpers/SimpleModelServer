namespace SimpleModelServer.Discovery;

/// <summary>
/// Represents a resolved connection to a SimpleModelServer instance.
/// </summary>
public sealed class ResolvedServerConnection
{
    /// <summary>
    /// Gets the base URL of the server (e.g., http://192.168.1.5:11435).
    /// Kept as a string rather than Uri for round-trip serialization through arbitrary persistence backends.
    /// </summary>
#pragma warning disable CA1056 // URI-like string properties should be of type Uri
    public required string BaseUrl { get; init; }
#pragma warning restore CA1056

    /// <summary>
    /// Gets a value indicating whether the connection is to a loopback address (localhost/127.0.0.1/::1).
    /// </summary>
    public required bool IsLoopback { get; init; }

    /// <summary>
    /// Gets the API key, if any (will be null for loopback connections).
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Gets the connection mode used to resolve this connection.
    /// </summary>
    public required ConnectionMode ResolvedVia { get; init; }
}
