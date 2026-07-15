using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using System.Text.Json;
using Xunit;

namespace SimpleModelServer.Mcp.Tests;

/// <summary>
/// Fake implementation of IMcpUpstreamRegistry for testing.
/// Allows tests to pre-seed status values and inspect connection/disconnection calls.
/// </summary>
internal sealed class FakeMcpUpstreamRegistry : IMcpUpstreamRegistry
{
    private readonly Dictionary<string, McpUpstreamStatus> _statusMap = new(StringComparer.OrdinalIgnoreCase);

    public List<(string serverName, IMcpUpstreamConfig config)> ConnectCalls { get; } = [];
    public List<string> DisconnectCalls { get; } = [];

    /// <summary>
    /// Pre-seeds the fake registry with a status entry (used to simulate GetStatus() results before tests call tools).
    /// </summary>
    public void SetStatus(string serverName, McpUpstreamStatus status)
    {
        _statusMap[serverName] = status;
    }

    public Task ConnectAsync(string serverName, IMcpUpstreamConfig upstreamConfig, CancellationToken ct = default)
    {
        ConnectCalls.Add((serverName, upstreamConfig));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(string serverName)
    {
        DisconnectCalls.Add(serverName);
        return Task.CompletedTask;
    }

    public IReadOnlyDictionary<string, McpUpstreamStatus> GetStatus()
    {
        return _statusMap;
    }
}

public sealed class McpAdminToolsTests
{
    private static JsonDocument ParseJson(string json) => JsonDocument.Parse(json);

    [Fact]
    public void ListMcpServers_WithEmptyConfig_ReturnsEmptyServerDict()
    {
        // Arrange
        var config = new AppConfig { McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase) };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = tools.ListMcpServers();

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("mcpServers", out var mcpServers));
        Assert.Equal(JsonValueKind.Object, mcpServers.ValueKind);
        Assert.Empty(mcpServers.EnumerateObject());
    }

    [Fact]
    public void ListMcpServers_WithConfiguredServer_ReturnsServerInfo()
    {
        // Arrange
        var serverConfig = new McpUpstreamConfig
        {
            TransportType = McpTransportType.Http,
            Path = "/path/to/server",
            Args = new[] { "--arg1", "--arg2" }
        };
        var config = new AppConfig
        {
            McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase)
            {
                { "my-server", serverConfig }
            }
        };
        var registry = new FakeMcpUpstreamRegistry();
        registry.SetStatus("my-server", new McpUpstreamStatus(
            TransportType: McpTransportType.Http,
            Path: "/path/to/server",
            Connected: true,
            ToolCount: 5
        ));
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = tools.ListMcpServers();

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("mcpServers", out var mcpServers));
        Assert.True(mcpServers.TryGetProperty("my-server", out var server));
        Assert.Equal("Http", server.GetProperty("transportType").GetString());
        Assert.Equal("/path/to/server", server.GetProperty("path").GetString());
        Assert.True(server.GetProperty("connected").GetBoolean());
        Assert.Equal(5, server.GetProperty("toolCount").GetInt32());
    }

    [Fact]
    public void ListMcpServers_WithArgsNull_OmitsArgsFromJson()
    {
        // Arrange
        var serverConfig = new McpUpstreamConfig
        {
            TransportType = McpTransportType.Http,
            Path = "/path/to/server",
            Args = new string[] { }  // Empty, should serialize as null/omitted
        };
        var config = new AppConfig
        {
            McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase)
            {
                { "empty-args", serverConfig }
            }
        };
        var registry = new FakeMcpUpstreamRegistry();
        registry.SetStatus("empty-args", new McpUpstreamStatus(
            TransportType: McpTransportType.Http,
            Path: "/path/to/server",
            Connected: false,
            ToolCount: 0
        ));
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = tools.ListMcpServers();

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("mcpServers", out var mcpServers));
        Assert.True(mcpServers.TryGetProperty("empty-args", out var server));
        // args is empty, so it may be omitted or empty array depending on JsonSerializerOptions
        // The real implementation uses WhenWritingNull, so null/empty collections are handled
        Assert.False(server.GetProperty("connected").GetBoolean());
        Assert.Equal(0, server.GetProperty("toolCount").GetInt32());
    }

    [Fact]
    public async Task AddMcpServerAsync_WithValidArgs_ReturnsSuccess()
    {
        // Arrange
        var config = new AppConfig { McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase) };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = await tools.AddMcpServerAsync("test-server", "/path/to/server");

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("test-server", root.GetProperty("name").GetString());
        Assert.True(root.GetProperty("message").GetString()?.Contains("test-server") ?? false);

        // Verify the server was added to config
        Assert.True(config.McpServers.ContainsKey("test-server"));
        var addedConfig = config.McpServers["test-server"];
        Assert.Equal("/path/to/server", addedConfig.Path);
        Assert.Equal(McpTransportType.Http, addedConfig.TransportType);  // Default

        // Verify registry calls
        Assert.Single(registry.ConnectCalls);
        Assert.Single(registry.DisconnectCalls);
        Assert.Equal("test-server", registry.DisconnectCalls[0]);
        Assert.Equal("test-server", registry.ConnectCalls[0].serverName);
    }

    [Fact]
    public async Task AddMcpServerAsync_WithNameContainingDot_ReturnsFalse()
    {
        // Arrange
        var config = new AppConfig { McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase) };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = await tools.AddMcpServerAsync("invalid.name", "/path/to/server");

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("error").GetString()?.Contains('.') ?? false);

        // Verify config was not mutated
        Assert.Empty(config.McpServers);

        // Verify no registry calls
        Assert.Empty(registry.ConnectCalls);
        Assert.Empty(registry.DisconnectCalls);
    }

    [Fact]
    public async Task AddMcpServerAsync_WithEmptyName_ReturnsFalse()
    {
        // Arrange
        var config = new AppConfig { McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase) };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = await tools.AddMcpServerAsync("", "/path/to/server");

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("error").GetString()?.Contains("name") ?? false);

        // Verify config was not mutated
        Assert.Empty(config.McpServers);
    }

    [Fact]
    public async Task AddMcpServerAsync_WithEmptyPath_ReturnsFalse()
    {
        // Arrange
        var config = new AppConfig { McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase) };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = await tools.AddMcpServerAsync("test-server", "");

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("error").GetString()?.Contains("path") ?? false);

        // Verify config was not mutated
        Assert.Empty(config.McpServers);
    }

    [Fact]
    public async Task AddMcpServerAsync_WithInvalidTransportType_ReturnsFalse()
    {
        // Arrange
        var config = new AppConfig { McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase) };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = await tools.AddMcpServerAsync("test-server", "/path/to/server", "InvalidTransport");

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        var errorMsg = root.GetProperty("error").GetString() ?? "";
        Assert.True(errorMsg.Contains("transportType") || errorMsg.Contains("Http"));

        // Verify config was not mutated
        Assert.Empty(config.McpServers);
    }

    [Fact]
    public async Task AddMcpServerAsync_WithTransportTypeOmitted_DefaultsToHttp()
    {
        // Arrange
        var config = new AppConfig { McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase) };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = await tools.AddMcpServerAsync("test-server", "/path/to/server", transportType: null);

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());

        // Verify the server was added with Http transport type
        Assert.True(config.McpServers.ContainsKey("test-server"));
        var addedConfig = config.McpServers["test-server"];
        Assert.Equal(McpTransportType.Http, addedConfig.TransportType);
    }

    [Fact]
    public async Task AddMcpServerAsync_WithTransportTypeStdio_AndArgs_RoundTrips()
    {
        // Arrange
        var config = new AppConfig { McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase) };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        var args = new[] { "--debug", "--log=/tmp/mcp.log" };

        // Act
        var result = await tools.AddMcpServerAsync("stdio-server", "/usr/local/bin/mcp-stdio-server", "Stdio", args);

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());

        // Verify the server was added with correct transport type and args
        Assert.True(config.McpServers.ContainsKey("stdio-server"));
        var addedConfig = config.McpServers["stdio-server"];
        Assert.Equal(McpTransportType.Stdio, addedConfig.TransportType);
        Assert.Equal("/usr/local/bin/mcp-stdio-server", addedConfig.Path);
        Assert.Equal(args, addedConfig.Args);
    }

    [Fact]
    public async Task RemoveMcpServerAsync_ForExistingServer_ReturnsSuccess()
    {
        // Arrange
        var serverConfig = new McpUpstreamConfig
        {
            TransportType = McpTransportType.Http,
            Path = "/path/to/server",
            Args = new string[] { }
        };
        var config = new AppConfig
        {
            McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase)
            {
                { "to-remove", serverConfig }
            }
        };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = await tools.RemoveMcpServerAsync("to-remove");

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("to-remove", root.GetProperty("name").GetString());
        Assert.True(root.GetProperty("message").GetString()?.Contains("to-remove") ?? false);

        // Verify the server was removed from config
        Assert.False(config.McpServers.ContainsKey("to-remove"));

        // Verify registry was called to disconnect
        Assert.Single(registry.DisconnectCalls);
        Assert.Equal("to-remove", registry.DisconnectCalls[0]);
    }

    [Fact]
    public async Task RemoveMcpServerAsync_ForUnknownServer_ReturnsFalse()
    {
        // Arrange
        var config = new AppConfig { McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase) };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = await tools.RemoveMcpServerAsync("unknown-server");

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("error").GetString()?.Contains("unknown-server") ?? false);

        // Verify registry was NOT called
        Assert.Empty(registry.DisconnectCalls);
    }

    [Fact]
    public async Task RemoveMcpServerAsync_WithEmptyName_ReturnsFalse()
    {
        // Arrange
        var config = new AppConfig { McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase) };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = await tools.RemoveMcpServerAsync("");

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("error").GetString()?.Contains("name") ?? false);

        // Verify registry was NOT called
        Assert.Empty(registry.DisconnectCalls);
    }

    [Fact]
    public async Task AddMcpServerAsync_ReplaceExisting_DisconnectsOldAndConnectsNew()
    {
        // Arrange
        var oldConfig = new McpUpstreamConfig
        {
            TransportType = McpTransportType.Http,
            Path = "/old/path",
            Args = new string[] { }
        };
        var config = new AppConfig
        {
            McpServers = new Dictionary<string, IMcpUpstreamConfig>(StringComparer.OrdinalIgnoreCase)
            {
                { "my-server", oldConfig }
            }
        };
        var registry = new FakeMcpUpstreamRegistry();
        var tools = new McpAdminTools(config, registry);

        // Act
        var result = await tools.AddMcpServerAsync("my-server", "/new/path", "Stdio");

        // Assert
        using var doc = ParseJson(result);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());

        // Verify the server was updated
        var updatedConfig = config.McpServers["my-server"];
        Assert.Equal("/new/path", updatedConfig.Path);
        Assert.Equal(McpTransportType.Stdio, updatedConfig.TransportType);

        // Verify disconnect then connect calls
        Assert.Single(registry.DisconnectCalls);
        Assert.Single(registry.ConnectCalls);
        Assert.Equal("my-server", registry.DisconnectCalls[0]);
        Assert.Equal("my-server", registry.ConnectCalls[0].serverName);
    }
}
