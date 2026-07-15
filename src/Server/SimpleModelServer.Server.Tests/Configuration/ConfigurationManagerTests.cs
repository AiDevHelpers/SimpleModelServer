using SimpleModelServer.Server.Configuration;
using Xunit;

namespace SimpleModelServer.Server.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IConfigurationManager"/> interface.
/// Validates configuration validation, restart requirements, and hot-reload capabilities.
/// </summary>
public class ConfigurationManagerTests
{
    private readonly IConfigurationManager _manager = new ConfigurationManager();

    /// <summary>
    /// Contract verification: IConfigurationManager has all required members.
    /// </summary>
    [Fact]
    public void IConfigurationManager_Interface_HasAllRequiredMembers()
    {
        // Act & Assert
        Assert.NotNull(_manager);
        // Methods should be callable
        _ = _manager.RequiresRestart("routerPort");
        _ = _manager.CanHotReload("defaultAgentType");
        _ = _manager.Validate("routerPort", 11435);
    }

    [Theory]
    [InlineData("routerPort", true)]
    [InlineData("router_port", true)]
    [InlineData("ollamaPort", true)]
    [InlineData("ollama_port", true)]
    [InlineData("routerHost", true)]
    [InlineData("draftModel", true)]
    [InlineData("mainModel", true)]
    // "provider" was never a real admin-updatable setting — Validate()/UpdateGlobalSetting()
    // both always rejected it as unknown, even though it was flagged restart-required. Dropped
    // from GlobalSettingRegistry (the single source of truth) as part of consolidating what was
    // four separately-maintained lists; RequiresRestart("provider") now consistently reports
    // false, like any other unrecognized setting name (see ConfigurationManagerTests below).
    public void RequiresRestart_WithRestartRequiredSettings_ReturnsTrue(string setting, bool expected)
    {
        // Act
        var result = _manager.RequiresRestart(setting);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("global.routerPort", true)]
    [InlineData("global.router_port", true)]
    [InlineData("global.draftModel", true)]
    public void RequiresRestart_WithFullPath_ReturnsTrue(string settingPath, bool expected)
    {
        // Act
        var result = _manager.RequiresRestart(settingPath);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("defaultAgentType", false)]
    [InlineData("default_agent_type", false)]
    [InlineData("trustedNetworks", false)]
    [InlineData("trusted_networks", false)]
    [InlineData("mdnsEnabled", false)]
    [InlineData("mdns_enabled", false)]
    public void RequiresRestart_WithHotReloadableSettings_ReturnsFalse(string setting, bool expected)
    {
        // Act
        var result = _manager.RequiresRestart(setting);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("defaultAgentType", true)]
    [InlineData("default_agent_type", true)]
    [InlineData("agentType", true)]
    [InlineData("agent_type", true)]
    [InlineData("trustedNetworks", true)]
    [InlineData("trusted_networks", true)]
    [InlineData("trustedApiKeys", true)]
    [InlineData("trusted_api_keys", true)]
    [InlineData("mdnsEnabled", true)]
    [InlineData("mdns_enabled", true)]
    public void CanHotReload_WithHotReloadableSettings_ReturnsTrue(string setting, bool expected)
    {
        // Act
        var result = _manager.CanHotReload(setting);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("routerPort", false)]
    [InlineData("router_port", false)]
    [InlineData("ollamaPort", false)]
    [InlineData("draftModel", false)]
    public void CanHotReload_WithRestartRequiredSettings_ReturnsFalse(string setting, bool expected)
    {
        // Act
        var result = _manager.CanHotReload(setting);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("routerPort", 11435, true)]
    [InlineData("routerPort", 1, true)]
    [InlineData("routerPort", 65535, true)]
    [InlineData("routerPort", 0, false)]
    [InlineData("routerPort", 65536, false)]
    [InlineData("routerPort", -1, false)]
    public void Validate_Port_ReturnsCorrectly(string setting, int port, bool shouldBeValid)
    {
        // Act
        var result = _manager.Validate(setting, port);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
    }

    [Theory]
    [InlineData("routerHost", "localhost", true)]
    [InlineData("routerHost", "127.0.0.1", true)]
    [InlineData("routerHost", "0.0.0.0", true)]
    [InlineData("routerHost", "", false)]
    [InlineData("routerHost", null, false)]
    public void Validate_NonEmptyString_ReturnsCorrectly(string setting, string? value, bool shouldBeValid)
    {
        // Act
        var result = _manager.Validate(setting, value);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
    }

    [Theory]
    [InlineData("defaultAgentType", "code", true)]
    [InlineData("defaultAgentType", "debug", true)]
    [InlineData("defaultAgentType", null, true)]
    [InlineData("defaultAgentType", "", true)]
    public void Validate_OptionalString_ReturnsCorrectly(string setting, string? value, bool shouldBeValid)
    {
        // Act
        var result = _manager.Validate(setting, value);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
    }

    [Theory]
    [InlineData("mdnsEnabled", true, true)]
    [InlineData("mdnsEnabled", false, true)]
    [InlineData("mdnsEnabled", "true", true)]
    [InlineData("mdnsEnabled", "false", true)]
    [InlineData("mdnsEnabled", null, false)]
    public void Validate_Boolean_ReturnsCorrectly(string setting, object? value, bool shouldBeValid)
    {
        // Act
        var result = _manager.Validate(setting, value);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
    }

    [Theory]
    [InlineData("contextSize", null, true)]
    [InlineData("contextSize", 2048, true)]
    [InlineData("contextSize", 4096, true)]
    [InlineData("contextSize", 0, true)]
    [InlineData("contextSize", -1, false)]
    public void Validate_NonNegativeInt_ReturnsCorrectly(string setting, int? value, bool shouldBeValid)
    {
        // Act
        var result = _manager.Validate(setting, value);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
    }

    [Fact]
    public void Validate_UnknownSetting_ReturnsInvalid()
    {
        // Act
        var result = _manager.Validate("unknownSetting", "value");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Unknown setting", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Port_WithString_ReturnsValid()
    {
        // Act
        var result = _manager.Validate("routerPort", "8080");

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Port_WithInvalidString_ReturnsInvalid()
    {
        // Act
        var result = _manager.Validate("routerPort", "not-a-port");

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidationResult_Valid_HasNoError()
    {
        // Act
        var result = _manager.Validate("defaultAgentType", "code");

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidationResult_Invalid_HasErrorMessage()
    {
        // Act
        var result = _manager.Validate("routerPort", -1);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.ErrorMessage);
    }
}
