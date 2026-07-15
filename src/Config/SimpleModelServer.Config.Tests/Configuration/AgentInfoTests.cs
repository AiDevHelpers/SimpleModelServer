using SimpleModelServer.Contracts.Dtos;
using Xunit;

namespace SimpleModelServer.Config.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IAgentInfo"/> interface.
/// Validates agent information properties.
/// </summary>
public class AgentInfoTests
{
    private class MockAgentInfo : IAgentInfo
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? DraftModel { get; init; }
        public string? MainModel { get; init; }
        public IReadOnlyDictionary<string, object?> Settings { get; init; } = new Dictionary<string, object?>();
    }

    /// <summary>
    /// Contract verification: IAgentInfo has all required members.
    /// </summary>
    [Fact]
    public void IAgentInfo_Interface_HasAllRequiredMembers()
    {
        // Arrange
        IAgentInfo info = new MockAgentInfo();

        // Act & Assert
        _ = info.Name;
        _ = info.Description;
        _ = info.DraftModel;
        _ = info.MainModel;
        _ = info.Settings;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("code-agent")]
    [InlineData("My Custom Agent")]
    public void Name_CanBeSet(string? value)
    {
        // Arrange & Act
        var info = new MockAgentInfo { Name = value };

        // Assert
        Assert.Equal(value, info.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("An agent for code generation")]
    [InlineData("Custom agent with specific capabilities")]
    public void Description_CanBeSet(string? value)
    {
        // Arrange & Act
        var info = new MockAgentInfo { Description = value };

        // Assert
        Assert.Equal(value, info.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("qwen2.5-0.5b-chat")]
    [InlineData("mistral-0.5b")]
    public void DraftModel_CanBeSet(string? value)
    {
        // Arrange & Act
        var info = new MockAgentInfo { DraftModel = value };

        // Assert
        Assert.Equal(value, info.DraftModel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("qwen2.5-3b-chat")]
    [InlineData("mistral-7b")]
    public void MainModel_CanBeSet(string? value)
    {
        // Arrange & Act
        var info = new MockAgentInfo { MainModel = value };

        // Assert
        Assert.Equal(value, info.MainModel);
    }

    [Fact]
    public void Settings_IsReadOnly()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            { "temperature", 0.7 },
            { "max_tokens", 256 }
        };
        var info = new MockAgentInfo { Settings = settings };

        // Act
        IReadOnlyDictionary<string, object?> readOnly = info.Settings;

        // Assert
        Assert.NotNull(readOnly);
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(readOnly);
    }

    [Fact]
    public void Settings_CanContainVariousTypes()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            { "temperature", 0.7 },
            { "max_tokens", 256 },
            { "enabled", true },
            { "name", "test" },
            { "null_value", null }
        };

        // Act
        var info = new MockAgentInfo { Settings = settings };

        // Assert
        Assert.Equal(5, info.Settings.Count);
        Assert.Equal(0.7, (double)info.Settings["temperature"]);
        Assert.Equal(256, (int)info.Settings["max_tokens"]);
        Assert.True((bool)info.Settings["enabled"]);
        Assert.Equal("test", (string)info.Settings["name"]);
        Assert.Null(info.Settings["null_value"]);
    }

    [Fact]
    public void AgentInfo_Default_HasEmptySettings()
    {
        // Arrange & Act
        var info = new MockAgentInfo();

        // Assert
        Assert.NotNull(info.Settings);
        Assert.Empty(info.Settings);
    }

    [Fact]
    public void AgentInfo_Complete_WithAllProperties()
    {
        // Arrange & Act
        var info = new MockAgentInfo
        {
            Name = "code-analyzer",
            Description = "Agent for code analysis and optimization",
            DraftModel = "qwen2.5-0.5b-chat",
            MainModel = "qwen2.5-3b-chat",
            Settings = new Dictionary<string, object?>
            {
                { "temperature", 0.5 },
                { "max_tokens", 512 }
            }
        };

        // Assert
        Assert.Equal("code-analyzer", info.Name);
        Assert.Equal("Agent for code analysis and optimization", info.Description);
        Assert.Equal("qwen2.5-0.5b-chat", info.DraftModel);
        Assert.Equal("qwen2.5-3b-chat", info.MainModel);
        Assert.Equal(2, info.Settings.Count);
    }

    [Fact]
    public void AgentInfo_InterfaceContract()
    {
        // Arrange
        IAgentInfo info = new MockAgentInfo
        {
            Name = "test-agent",
            Description = "A test agent",
            DraftModel = "model-0.5b",
            MainModel = "model-3b",
            Settings = new Dictionary<string, object?>()
        };

        // Act & Assert
        Assert.Equal("test-agent", info.Name);
        Assert.Equal("A test agent", info.Description);
        Assert.Equal("model-0.5b", info.DraftModel);
        Assert.Equal("model-3b", info.MainModel);
        Assert.Empty(info.Settings);
    }

    [Fact]
    public void AgentInfo_NullProperties()
    {
        // Arrange & Act
        var info = new MockAgentInfo
        {
            Name = null,
            Description = null,
            DraftModel = null,
            MainModel = null
        };

        // Assert
        Assert.Null(info.Name);
        Assert.Null(info.Description);
        Assert.Null(info.DraftModel);
        Assert.Null(info.MainModel);
    }

    [Fact]
    public void AgentInfo_SettingsAccessibility()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            { "key1", "value1" },
            { "key2", 42 }
        };

        // Act
        var info = new MockAgentInfo { Settings = settings };

        // Assert
        Assert.True(info.Settings.ContainsKey("key1"));
        Assert.True(info.Settings.ContainsKey("key2"));
        Assert.Equal("value1", (string?)info.Settings["key1"]);
        Assert.Equal(42, (int?)info.Settings["key2"]);
    }
}
