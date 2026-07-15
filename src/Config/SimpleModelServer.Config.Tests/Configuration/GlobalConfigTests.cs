using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using Xunit;

namespace SimpleModelServer.Config.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IGlobalConfig"/> interface and <see cref="GlobalConfig"/> implementation.
/// Validates all configuration properties and their default values.
/// </summary>
public class GlobalConfigTests
{
    /// <summary>
    /// Contract verification: IGlobalConfig has all required properties.
    /// </summary>
    [Fact]
    public void IGlobalConfig_Interface_HasAllRequiredProperties()
    {
        // Arrange
        IGlobalConfig config = new GlobalConfig();

        // Act & Assert - All properties must be readable without throwing
        _ = config.DefaultAgentType;
        _ = config.GpuLayers;
        _ = config.CpuThreads;
        _ = config.DraftModel;
        _ = config.MainModel;
        _ = config.EmbedModel;
        _ = config.OllamaPort;
        _ = config.RouterPort;
        _ = config.RouterHost;
        _ = config.RouterModelName;
        _ = config.ContextSize;
        _ = config.AgentType;
        _ = config.DraftHandoffTimeoutMs;
        _ = config.DraftMode;
        _ = config.ModelSourcesPath;
        _ = config.TrustedNetworks;
        _ = config.TrustedApiKeys;
        _ = config.MdnsEnabled;
        _ = config.MdnsServiceName;
        _ = config.WindowsServiceName;
        _ = config.Debug;
    }

    [Fact]
    public void GlobalConfig_Default_HasValidDefaults()
    {
        // Arrange & Act
        var config = new GlobalConfig();

        // Assert
        Assert.Equal("code", config.DefaultAgentType);
        Assert.Equal("", config.DraftModel);
        Assert.Equal("", config.MainModel);
        Assert.Equal("", config.EmbedModel);
        Assert.Equal(11434, config.OllamaPort);
        Assert.Equal(11435, config.RouterPort);
        Assert.Equal("localhost", config.RouterHost);
        Assert.Equal("HelperModel", config.RouterModelName);
        Assert.Null(config.ContextSize);
        Assert.Equal("", config.AgentType);
        Assert.Equal(900, config.DraftHandoffTimeoutMs);
        Assert.Equal(DraftMode.Preview, config.DraftMode);
        Assert.Equal("", config.ModelSourcesPath);
        Assert.Empty(config.TrustedNetworks);
        Assert.Empty(config.TrustedApiKeys);
        Assert.True(config.MdnsEnabled);
        Assert.Equal("", config.MdnsServiceName);
        Assert.Equal("SimpleModelServerLoader", config.WindowsServiceName);
        Assert.NotNull(config.Debug);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("ask")]
    [InlineData("debug")]
    public void DefaultAgentType_CanBeSet(string agentType)
    {
        // Arrange & Act
        var config = new GlobalConfig { DefaultAgentType = agentType };

        // Assert
        Assert.Equal(agentType, config.DefaultAgentType);
    }

    [Theory]
    [InlineData(11434)]
    [InlineData(8000)]
    [InlineData(65535)]
    public void OllamaPort_CanBeSet(int port)
    {
        // Arrange & Act
        var config = new GlobalConfig { OllamaPort = port };

        // Assert
        Assert.Equal(port, config.OllamaPort);
    }

    [Theory]
    [InlineData(11435)]
    [InlineData(8001)]
    [InlineData(9999)]
    public void RouterPort_CanBeSet(int port)
    {
        // Arrange & Act
        var config = new GlobalConfig { RouterPort = port };

        // Assert
        Assert.Equal(port, config.RouterPort);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("+")]
    [InlineData("*")]
    public void RouterHost_CanBeSet(string host)
    {
        // Arrange & Act
        var config = new GlobalConfig { RouterHost = host };

        // Assert
        Assert.Equal(host, config.RouterHost);
    }

    [Fact]
    public void ContextSize_CanBeSetToNull()
    {
        // Arrange & Act
        var config = new GlobalConfig { ContextSize = null };

        // Assert
        Assert.Null(config.ContextSize);
    }

    [Theory]
    [InlineData(2048)]
    [InlineData(4096)]
    [InlineData(8192)]
    public void ContextSize_CanBeSetToValue(int? size)
    {
        // Arrange & Act
        var config = new GlobalConfig { ContextSize = size };

        // Assert
        Assert.Equal(size, config.ContextSize);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(2000)]
    public void DraftHandoffTimeoutMs_CanBeSet(int timeout)
    {
        // Arrange & Act
        var config = new GlobalConfig { DraftHandoffTimeoutMs = timeout };

        // Assert
        Assert.Equal(timeout, config.DraftHandoffTimeoutMs);
    }

    [Theory]
    [InlineData(DraftMode.Preview)]
    [InlineData(DraftMode.Cleanup)]
    [InlineData(DraftMode.Both)]
    public void DraftMode_CanBeSet(DraftMode mode)
    {
        // Arrange & Act
        var config = new GlobalConfig { DraftMode = mode };

        // Assert
        Assert.Equal(mode, config.DraftMode);
    }

    [Fact]
    public void TrustedNetworks_IsReadOnly()
    {
        // Arrange
        var trustedNetworks = new[] { "192.168.0.0", "10.0.0.0/8" };
        var config = new GlobalConfig { TrustedNetworks = trustedNetworks };

        // Act
        IReadOnlyList<string> readOnly = config.TrustedNetworks;

        // Assert
        Assert.NotNull(readOnly);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(readOnly);
        Assert.Equal(2, readOnly.Count);
    }

    [Fact]
    public void TrustedApiKeys_IsReadOnly()
    {
        // Arrange
        var apiKeys = new[] { "key1", "key2", "key3" };
        var config = new GlobalConfig { TrustedApiKeys = apiKeys };

        // Act
        IReadOnlyList<string> readOnly = config.TrustedApiKeys;

        // Assert
        Assert.NotNull(readOnly);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(readOnly);
        Assert.Equal(3, readOnly.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MdnsEnabled_CanBeSet(bool enabled)
    {
        // Arrange & Act
        var config = new GlobalConfig { MdnsEnabled = enabled };

        // Assert
        Assert.Equal(enabled, config.MdnsEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CustomService")]
    [InlineData("MyServerInstance")]
    public void MdnsServiceName_CanBeSet(string serviceName)
    {
        // Arrange & Act
        var config = new GlobalConfig { MdnsServiceName = serviceName };

        // Assert
        Assert.Equal(serviceName, config.MdnsServiceName);
    }

    [Fact]
    public void Debug_IsDebugConfig()
    {
        // Arrange & Act
        var config = new GlobalConfig();

        // Assert
        Assert.NotNull(config.Debug);
        Assert.IsAssignableFrom<IDebugConfig>(config.Debug);
    }

    [Fact]
    public void GlobalConfig_IsImmutable_ViaInit()
    {
        // Arrange & Act
        var config = new GlobalConfig
        {
            DefaultAgentType = "test",
            OllamaPort = 12345,
            MdnsEnabled = false
        };

        // Assert - Properties cannot be modified after initialization
        // (This is enforced at compile-time by the init keyword)
        Assert.Equal("test", config.DefaultAgentType);
        Assert.Equal(12345, config.OllamaPort);
        Assert.False(config.MdnsEnabled);
    }

    [Fact]
    public void GlobalConfig_CanBeUsedAsInterface()
    {
        // Arrange
        IGlobalConfig config = new GlobalConfig
        {
            DefaultAgentType = "custom",
            OllamaPort = 9999
        };

        // Act & Assert
        Assert.Equal("custom", config.DefaultAgentType);
        Assert.Equal(9999, config.OllamaPort);
    }

    [Fact]
    public void HardwareSetting_Unspecified_HasExpectedDefaults()
    {
        // Arrange & Act
        var config = new GlobalConfig();

        // Assert
        Assert.False(config.GpuLayers.IsSpecified);
        Assert.False(config.CpuThreads.IsSpecified);
    }

    [Fact]
    public void ModelSourcesPath_CanBeSet()
    {
        // Arrange
        var path = "C:\\config\\models.json";

        // Act
        var config = new GlobalConfig { ModelSourcesPath = path };

        // Assert
        Assert.Equal(path, config.ModelSourcesPath);
    }
}
