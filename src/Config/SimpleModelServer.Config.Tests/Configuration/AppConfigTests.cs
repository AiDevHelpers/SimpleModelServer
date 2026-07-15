using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Agents;
using SimpleModelServer.Contracts.Configuration;
using SimpleModelServer.Contracts.Dtos.Providers;
using SimpleModelServer.Contracts.Providers;
using Xunit;

namespace SimpleModelServer.Config.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IAppConfig"/> interface and <see cref="AppConfig"/> implementation.
/// Validates configuration loading, resolution, and agent type handling.
/// </summary>
public class AppConfigTests
{
    /// <summary>
    /// Contract verification: IAppConfig has all required members.
    /// </summary>
    [Fact]
    public void IAppConfig_Interface_HasRequiredMembers()
    {
        // Arrange
        var config = new AppConfig();

        // Act & Assert - interface members exist and are accessible
        _ = config.Global;
        _ = config.Agents;
        _ = config.ModelSources;
        _ = config.ModelRoles;
        _ = config.DefaultAgentType;

        // All methods should be callable without throwing
        _ = config.ResolveEmbedModelName();
        _ = config.IsEmbedModel("test");
        _ = config.IsKnownAgentType("test");
        _ = config.Agent("test");
    }

    [Fact]
    public void AppConfig_Default_HasValidDefaults()
    {
        // Arrange & Act
        var config = new AppConfig();

        // Assert
        Assert.NotNull(config.Global);
        Assert.NotNull(config.Agents);
        Assert.NotNull(config.ModelSources);
        Assert.NotNull(config.ModelRoles);
        Assert.NotEmpty(config.DefaultAgentType);
    }

    [Fact]
    public void Load_NoProviderSection_DefaultsToOllama()
    {
        // Arrange
        string path = WriteTempConfig("""{ "global": { "routerPort": 11435 } }""");

        try
        {
            // Act
            AppConfig config = AppConfig.Load(path);

            // Assert — every existing config predates the "provider" section, so its absence
            // must keep behaving exactly like Ollama on the default port.
            Assert.Equal(Provider.Ollama, config.Global.Provider.Type);
            Assert.Equal("127.0.0.1", config.Global.Provider.Host);
            Assert.Equal(11434, config.Global.Provider.Port);
            Assert.Null(((ProviderConfiguration)config.Global.Provider).ApiKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ProviderSectionLMStudio_ParsesTypeHostPortApiKey()
    {
        // Arrange
        string path = WriteTempConfig("""
            {
                "global": {
                    "provider": { "type": "lmstudio", "host": "192.168.1.50", "port": 1234, "apiKey": "secret" }
                }
            }
            """);

        try
        {
            // Act
            AppConfig config = AppConfig.Load(path);

            // Assert
            Assert.Equal(Provider.LMStudio, config.Global.Provider.Type);
            Assert.Equal("192.168.1.50", config.Global.Provider.Host);
            Assert.Equal(1234, config.Global.Provider.Port);
            Assert.Equal("secret", ((ProviderConfiguration)config.Global.Provider).ApiKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ProviderTypeLMStudioNoPort_DefaultsToLMStudioConventionalPort()
    {
        // Arrange
        string path = WriteTempConfig("""{ "global": { "provider": { "type": "lmstudio" } } }""");

        try
        {
            // Act
            AppConfig config = AppConfig.Load(path);

            // Assert — 1234 is LM Studio's conventional default port, distinct from Ollama's 11434.
            Assert.Equal(1234, config.Global.Provider.Port);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempConfig(string json)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void DefaultAgentType_FallsBackToAskWhenGlobalIsEmpty()
    {
        // Arrange
        var config = new AppConfig
        {
            Global = new GlobalConfig { DefaultAgentType = "" }
        };

        // Act
        var result = config.DefaultAgentType;

        // Assert — "ask" matches DefaultConfig.cs's shipped out-of-the-box default; GlobalConfig's
        // own bare-constructor default ("code") is a separate, deliberately different fallback
        // used when no explicit config value is provided at all (see GlobalConfig.DefaultAgentType).
        Assert.Equal("ask", result);
    }

    [Fact]
    public void DefaultAgentType_UsesGlobalWhenSet()
    {
        // Arrange
        var globalConfig = new GlobalConfig { DefaultAgentType = "custom-agent" };
        var config = new AppConfig { Global = globalConfig };

        // Act
        var result = config.DefaultAgentType;

        // Assert
        Assert.Equal("custom-agent", result);
    }

    [Fact]
    public void ResolveEmbedModelName_UsesExplicitEmbedModelWhenSet()
    {
        // Arrange
        var config = new AppConfig
        {
            Global = new GlobalConfig { EmbedModel = "explicit-embed" },
            ModelRoles = new Dictionary<string, string> { { "embed", "role-embed" } }
        };

        // Act
        var result = config.ResolveEmbedModelName();

        // Assert
        Assert.Equal("explicit-embed", result);
    }

    [Fact]
    public void ResolveEmbedModelName_FallsBackToModelRoleWhenExplicitNotSet()
    {
        // Arrange
        var config = new AppConfig
        {
            Global = new GlobalConfig { EmbedModel = "" },
            ModelRoles = new Dictionary<string, string> { { "embed", "role-embed" } }
        };

        // Act
        var result = config.ResolveEmbedModelName();

        // Assert
        Assert.Equal("role-embed", result);
    }

    [Fact]
    public void ResolveEmbedModelName_FallsBackToDefaultWhenNothingSet()
    {
        // Arrange
        var config = new AppConfig
        {
            Global = new GlobalConfig { EmbedModel = "" },
            ModelRoles = new Dictionary<string, string>()
        };

        // Act
        var result = config.ResolveEmbedModelName();

        // Assert
        Assert.Equal("nomic-embed-text", result);
    }

    [Theory]
    [InlineData("explicit-embed", true)]
    [InlineData("other-model", false)]
    [InlineData("", false)]
    public void IsEmbedModel_CorrectlyIdentifiesEmbedModel(string modelName, bool expectedResult)
    {
        // Arrange
        var config = new AppConfig
        {
            Global = new GlobalConfig { EmbedModel = "explicit-embed" },
            ModelRoles = new Dictionary<string, string>()
        };

        // Act
        var result = config.IsEmbedModel(modelName);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData("ask", true)]
    [InlineData("debug", true)]
    [InlineData("code", true)]
    [InlineData("planning", true)]
    [InlineData("custom-agent", false)]
    public void IsKnownAgentType_RecognizesDefaultAgentTypes(string agentType, bool expectedResult)
    {
        // Arrange
        var config = new AppConfig
        {
            Agents = new Dictionary<string, IAgentConfig>(StringComparer.OrdinalIgnoreCase)
        };

        // Act
        var result = config.IsKnownAgentType(agentType);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void IsKnownAgentType_RecognizesConfiguredAgents()
    {
        // Arrange
        var customAgent = new SimpleModelServer.Contracts.Dtos.Agents.AgentConfig();
        var config = new AppConfig
        {
            Agents = new Dictionary<string, IAgentConfig>(StringComparer.OrdinalIgnoreCase)
            {
                { "custom-agent", customAgent }
            }
        };

        // Act & Assert
        Assert.True(config.IsKnownAgentType("custom-agent"));
        Assert.True(config.IsKnownAgentType("CUSTOM-AGENT")); // Case-insensitive
    }

    [Fact]
    public void Agent_ReturnsDefaultAgentConfigWhenNotFound()
    {
        // Arrange
        var config = new AppConfig
        {
            Agents = new Dictionary<string, IAgentConfig>(StringComparer.OrdinalIgnoreCase)
        };

        // Act
        var result = config.Agent("non-existent");

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IAgentConfig>(result);
    }

    [Fact]
    public void Agent_ReturnsPreconfiguredAgentWhenFound()
    {
        // Arrange
        var customAgent = new SimpleModelServer.Contracts.Dtos.Agents.AgentConfig
        {
            SystemPrompt = "Test prompt"
        };
        var config = new AppConfig
        {
            Agents = new Dictionary<string, IAgentConfig>(StringComparer.OrdinalIgnoreCase)
            {
                { "test-agent", customAgent }
            }
        };

        // Act
        var result = config.Agent("test-agent");

        // Assert
        Assert.Same(customAgent, result);
        Assert.Equal("Test prompt", result.SystemPrompt);
    }

    [Fact]
    public void Agent_IsCaseInsensitive()
    {
        // Arrange
        var customAgent = new SimpleModelServer.Contracts.Dtos.Agents.AgentConfig
        {
            SystemPrompt = "Test prompt"
        };
        var config = new AppConfig
        {
            Agents = new Dictionary<string, IAgentConfig>(StringComparer.OrdinalIgnoreCase)
            {
                { "TestAgent", customAgent }
            }
        };

        // Act
        var result = config.Agent("testagent");

        // Assert
        Assert.Same(customAgent, result);
    }

    [Fact]
    public void IAppConfig_ModelSources_ReturnsReadOnly()
    {
        // Arrange
        var config = new AppConfig
        {
            ModelSources = new Dictionary<string, IModelSourceConfig>(StringComparer.OrdinalIgnoreCase)
        };

        // Act
        IReadOnlyDictionary<string, IModelSourceConfig> readOnly = config.ModelSources;

        // Assert
        Assert.NotNull(readOnly);
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, IModelSourceConfig>>(readOnly);
    }

    [Fact]
    public void IAppConfig_Agents_ReturnsReadOnly()
    {
        // Arrange
        var config = new AppConfig
        {
            Agents = new Dictionary<string, IAgentConfig>(StringComparer.OrdinalIgnoreCase)
        };

        // Act
        IReadOnlyDictionary<string, IAgentConfig> readOnly = config.Agents;

        // Assert
        Assert.NotNull(readOnly);
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, IAgentConfig>>(readOnly);
    }
}
