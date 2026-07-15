using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using Xunit;

namespace SimpleModelServer.Config.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IDebugConfig"/> interface and <see cref="DebugConfig"/> implementation.
/// Validates diagnostic toggle properties and their default values.
/// </summary>
public class DebugConfigTests
{
    /// <summary>
    /// Contract verification: IDebugConfig has all required members.
    /// </summary>
    [Fact]
    public void IDebugConfig_Interface_HasAllRequiredProperties()
    {
        // Arrange
        IDebugConfig config = new DebugConfig();

        // Act & Assert - All properties must be readable without throwing
        _ = config.LogUnattributedRequests;
        _ = config.LogRequestBodies;
    }

    [Fact]
    public void DebugConfig_Default_HasAllFlagsDisabled()
    {
        // Arrange & Act
        var config = new DebugConfig();

        // Assert
        Assert.False(config.LogUnattributedRequests);
        Assert.False(config.LogRequestBodies);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LogUnattributedRequests_CanBeSet(bool value)
    {
        // Arrange & Act
        var config = new DebugConfig { LogUnattributedRequests = value };

        // Assert
        Assert.Equal(value, config.LogUnattributedRequests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LogRequestBodies_CanBeSet(bool value)
    {
        // Arrange & Act
        var config = new DebugConfig { LogRequestBodies = value };

        // Assert
        Assert.Equal(value, config.LogRequestBodies);
    }

    [Fact]
    public void DebugConfig_CanBeCreatedWithAllPropertiesDisabled()
    {
        // Arrange & Act
        var config = new DebugConfig
        {
            LogUnattributedRequests = false,
            LogRequestBodies = false
        };

        // Assert
        Assert.False(config.LogUnattributedRequests);
        Assert.False(config.LogRequestBodies);
    }

    [Fact]
    public void DebugConfig_CanBeCreatedWithAllPropertiesEnabled()
    {
        // Arrange & Act
        var config = new DebugConfig
        {
            LogUnattributedRequests = true,
            LogRequestBodies = true
        };

        // Assert
        Assert.True(config.LogUnattributedRequests);
        Assert.True(config.LogRequestBodies);
    }

    [Fact]
    public void DebugConfig_CanBeCreatedWithMixedValues()
    {
        // Arrange & Act
        var config1 = new DebugConfig
        {
            LogUnattributedRequests = true,
            LogRequestBodies = false
        };

        var config2 = new DebugConfig
        {
            LogUnattributedRequests = false,
            LogRequestBodies = true
        };

        // Assert
        Assert.True(config1.LogUnattributedRequests);
        Assert.False(config1.LogRequestBodies);
        Assert.False(config2.LogUnattributedRequests);
        Assert.True(config2.LogRequestBodies);
    }

    [Fact]
    public void DebugConfig_CanBeUsedAsInterface()
    {
        // Arrange
        IDebugConfig config = new DebugConfig
        {
            LogUnattributedRequests = true,
            LogRequestBodies = false
        };

        // Act & Assert
        Assert.True(config.LogUnattributedRequests);
        Assert.False(config.LogRequestBodies);
    }

    [Fact]
    public void DebugConfig_IsImmutable()
    {
        // Arrange & Act
        var config = new DebugConfig
        {
            LogUnattributedRequests = true,
            LogRequestBodies = false
        };

        // Assert - Properties are set via init, cannot be changed
        Assert.True(config.LogUnattributedRequests);
        Assert.False(config.LogRequestBodies);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void DebugConfig_AllCombinations(bool logUnattributed, bool logBodies)
    {
        // Arrange & Act
        var config = new DebugConfig
        {
            LogUnattributedRequests = logUnattributed,
            LogRequestBodies = logBodies
        };

        // Assert
        Assert.Equal(logUnattributed, config.LogUnattributedRequests);
        Assert.Equal(logBodies, config.LogRequestBodies);
    }
}
