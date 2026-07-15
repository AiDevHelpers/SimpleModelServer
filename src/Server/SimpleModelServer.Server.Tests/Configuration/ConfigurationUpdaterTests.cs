using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Server.Configuration;
using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IConfigurationUpdater"/> interface.
/// Validates configuration updates and restart requirement determination.
/// </summary>
public class ConfigurationUpdaterTests
{
    private readonly IConfigurationManager _configManager = new ConfigurationManager();
    private readonly IConfigurationUpdater _updater;

    public ConfigurationUpdaterTests()
    {
        _updater = new ConfigurationUpdater(_configManager);
    }

    /// <summary>
    /// Contract verification: IConfigurationUpdater has all required members.
    /// </summary>
    [Fact]
    public void IConfigurationUpdater_Interface_HasAllRequiredMembers()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act & Assert - Method should be callable
        var result = _updater.UpdateGlobalSetting(config, "defaultAgentType", "code");
        Assert.NotNull(result);
    }

    [Fact]
    public void UpdateGlobalSetting_DefaultAgentType_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "defaultAgentType", "debug");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void UpdateGlobalSetting_DefaultAgentType_DoesNotRequireRestart()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "defaultAgentType", "code");

        // Assert
        Assert.False(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_AgentType_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "agentType", "planning");

        // Assert
        Assert.True(result.Success);
        Assert.False(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_RouterPort_RequiresRestart()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "routerPort", 9999);

        // Assert
        Assert.True(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_OllamaPort_RequiresRestart()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "ollamaPort", 12434);

        // Assert
        Assert.True(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_RouterHost_RequiresRestart()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "routerHost", "0.0.0.0");

        // Assert
        Assert.True(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_DraftModel_RequiresRestart()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "draftModel", "qwen2.5-0.5b-chat");

        // Assert
        Assert.True(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_MainModel_RequiresRestart()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "mainModel", "qwen2.5-3b-chat");

        // Assert
        Assert.True(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_TrustedNetworks_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "trustedNetworks", new[] { "192.168.0.0", "10.0.0.0" });

        // Assert
        Assert.True(result.Success);
        Assert.False(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_TrustedApiKeys_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "trustedApiKeys", new[] { "key1", "key2" });

        // Assert
        Assert.True(result.Success);
        Assert.False(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_MdnsEnabled_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "mdnsEnabled", false);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_MdnsServiceName_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "mdnsServiceName", "CustomService");

        // Assert
        Assert.True(result.Success);
        Assert.False(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_ContextSize_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "contextSize", 8192);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_DraftHandoffTimeoutMs_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "draftHandoffTimeoutMs", 1500);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_DraftMode_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "draftMode", "cleanup");

        // Assert
        Assert.True(result.Success);
        Assert.False(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_EmbedModel_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "embedModel", "nomic-embed-text");

        // Assert
        Assert.True(result.Success);
        Assert.False(result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_UnknownSetting_Fails()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "unknownSetting", "value");

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void UpdateResult_Success_HasNoError()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "defaultAgentType", "code");

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void UpdateResult_Failure_HasErrorMessage()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "unknownSetting", "value");

        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.ErrorMessage);
    }

    [Theory]
    [InlineData("defaultAgentType", "code", false)]
    [InlineData("defaultAgentType", "ask", false)]
    [InlineData("defaultAgentType", "debug", false)]
    [InlineData("agentType", "planning", false)]
    [InlineData("mdnsEnabled", true, false)]
    [InlineData("mdnsEnabled", false, false)]
    public void UpdateGlobalSetting_HotReloadableSettings_DoNotRequireRestart(
        string settingName,
        object value,
        bool expectedRestart)
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, settingName, value);

        // Assert
        Assert.Equal(expectedRestart, result.RequiresRestart);
    }

    [Theory]
    [InlineData("routerPort", 8080, true)]
    [InlineData("ollamaPort", 11434, true)]
    [InlineData("draftModel", "model-name", true)]
    [InlineData("mainModel", "model-name", true)]
    public void UpdateGlobalSetting_RestartRequiredSettings_RequireRestart(
        string settingName,
        object value,
        bool expectedRestart)
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, settingName, value);

        // Assert
        Assert.Equal(expectedRestart, result.RequiresRestart);
    }

    [Fact]
    public void UpdateGlobalSetting_WithNullValue_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "defaultAgentType", null);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void UpdateGlobalSetting_WithStringPortValue_Success()
    {
        // Arrange
        var config = new GlobalConfig();

        // Act
        var result = _updater.UpdateGlobalSetting(config, "routerPort", "9999");

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RequiresRestart);
    }
}
