namespace SimpleModelServer.Discovery;

/// <summary>
/// Persisted settings for server connection preferences.
/// </summary>
public sealed class ServerConnectionSettings
{
    /// <summary>
    /// Gets or sets the connection mode (Local, Mdns, or Manual).
    /// </summary>
    public ConnectionMode Mode { get; set; } = ConnectionMode.Local;

    /// <summary>
    /// Gets or sets the manual host name or IP address (used when Mode is Manual).
    /// </summary>
    public string? ManualHost { get; set; }

    /// <summary>
    /// Gets or sets the manual port (used when Mode is Manual or as fallback for Local).
    /// </summary>
    public int ManualPort { get; set; } = 11435;

    /// <summary>
    /// Gets or sets the remembered mDNS instance name (used when Mode is Mdns to prefer a specific discovered instance).
    /// </summary>
    public string? RememberedMdnsInstanceName { get; set; }

    /// <summary>
    /// Gets or sets the API key to send to the server (sent as Bearer token when not connecting to loopback).
    /// </summary>
    public string? ApiKey { get; set; }
}
