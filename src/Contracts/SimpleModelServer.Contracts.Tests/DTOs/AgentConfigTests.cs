using System.Text.Json.Nodes;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Dtos.Agents;
using Xunit;

namespace SimpleModelServer.Contracts.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IAgentConfig"/> interface.
/// Validates agent configuration including system prompts, temperature, and sampling parameters.
/// </summary>
public class AgentConfigTests
{
    /// <summary>
    /// Contract verification: IAgentConfig has all required members.
    /// </summary>
    [Fact]
    public void IAgentConfig_Interface_HasRequiredMembers()
    {
        // Arrange
        IAgentConfig config = CreateTestAgentConfig();

        // Act & Assert - All properties must be readable without throwing
        _ = config.SystemPrompt;
        _ = config.Temperature;
        _ = config.Sampling;
    }

    [Fact]
    public void AgentConfig_Default_HasValidDefaults()
    {
        // Arrange & Act
        IAgentConfig config = CreateTestAgentConfig();

        // Assert
        Assert.NotNull(config.SystemPrompt);
        // Temperature and Sampling can be null (optional)
    }

    [Theory]
    [InlineData("")]
    [InlineData("You are a helpful AI assistant")]
    [InlineData("Think carefully before responding")]
    [InlineData("Provide detailed, step-by-step explanations")]
    public void AgentConfig_SystemPrompt_CanHaveDifferentValues(string prompt)
    {
        // Arrange & Act
        IAgentConfig config = CreateTestAgentConfig(systemPrompt: prompt);

        // Assert
        Assert.Equal(prompt, config.SystemPrompt);
    }

    [Fact]
    public void AgentConfig_Temperature_CanBeNull()
    {
        // Arrange & Act
        IAgentConfig config = CreateTestAgentConfig(temperature: null);

        // Assert
        Assert.Null(config.Temperature);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void AgentConfig_Temperature_CanHaveDifferentValues(double temperature)
    {
        // Arrange & Act
        IAgentConfig config = CreateTestAgentConfig(temperature: temperature);

        // Assert
        Assert.Equal(temperature, config.Temperature);
    }

    [Fact]
    public void AgentConfig_Temperature_ZeroIsValid()
    {
        // Arrange & Act
        IAgentConfig config = CreateTestAgentConfig(temperature: 0.0);

        // Assert
        Assert.Equal(0.0, config.Temperature);
    }

    [Fact]
    public void AgentConfig_Sampling_CanBeNull()
    {
        // Arrange & Act
        IAgentConfig config = CreateTestAgentConfig(sampling: null);

        // Assert
        Assert.Null(config.Sampling);
    }

    [Fact]
    public void AgentConfig_Sampling_CanContainJsonParameters()
    {
        // Arrange
        var samplingNode = new JsonObject
        {
            { "top_p", 0.95 },
            { "top_k", 40 },
            { "repeat_penalty", 1.1 }
        };

        // Act
        IAgentConfig config = CreateTestAgentConfig(sampling: samplingNode);

        // Assert
        Assert.NotNull(config.Sampling);
        Assert.IsType<JsonObject>(config.Sampling);
    }

    [Fact]
    public void AgentConfig_Sampling_IsMutable()
    {
        // Arrange
        var samplingNode = new JsonObject();
        IAgentConfig config = CreateTestAgentConfig(sampling: samplingNode);

        // Act
        config.Sampling!["new_param"] = "value";

        // Assert
        Assert.NotNull(config.Sampling);
        Assert.Contains("new_param", config.Sampling.AsObject().Keys);
    }

    [Fact]
    public void AgentConfig_AllPropertiesCanBeConfigured()
    {
        // Arrange
        var sampling = new JsonObject { { "top_p", 0.9 } };

        // Act
        IAgentConfig config = CreateTestAgentConfig(
            systemPrompt: "Custom system prompt",
            temperature: 0.7,
            sampling: sampling
        );

        // Assert
        Assert.Equal("Custom system prompt", config.SystemPrompt);
        Assert.Equal(0.7, config.Temperature);
        Assert.NotNull(config.Sampling);
    }

    [Fact]
    public void AgentConfig_SystemPrompt_CanBeEmpty()
    {
        // Arrange & Act
        IAgentConfig config = CreateTestAgentConfig(systemPrompt: "");

        // Assert
        Assert.Equal("", config.SystemPrompt);
    }

    [Fact]
    public void AgentConfig_SystemPrompt_CanBeLong()
    {
        // Arrange
        var longPrompt = string.Concat(Enumerable.Repeat("This is a long prompt. ", 100));

        // Act
        IAgentConfig config = CreateTestAgentConfig(systemPrompt: longPrompt);

        // Assert
        Assert.Equal(longPrompt, config.SystemPrompt);
    }

    [Fact]
    public void AgentConfig_TemperatureEdgeCases()
    {
        // Arrange & Act
        var coldConfig = CreateTestAgentConfig(temperature: 0.1);
        var warmConfig = CreateTestAgentConfig(temperature: 10.0);

        // Assert
        Assert.Equal(0.1, coldConfig.Temperature);
        Assert.Equal(10.0, warmConfig.Temperature);
    }

    [Fact]
    public void AgentConfig_MultipleConfigs_AreIndependent()
    {
        // Arrange & Act
        var config1 = CreateTestAgentConfig(systemPrompt: "Prompt 1", temperature: 0.5);
        var config2 = CreateTestAgentConfig(systemPrompt: "Prompt 2", temperature: 0.8);

        // Assert
        Assert.NotEqual(config1.SystemPrompt, config2.SystemPrompt);
        Assert.NotEqual(config1.Temperature, config2.Temperature);
    }

    // Helper to create test implementations
    private static IAgentConfig CreateTestAgentConfig(
        string systemPrompt = "",
        double? temperature = null,
        JsonNode? sampling = null)
    {
        return new AgentConfig
        {
            SystemPrompt = systemPrompt,
            Temperature = temperature,
            Sampling = sampling
        };
    }
}
