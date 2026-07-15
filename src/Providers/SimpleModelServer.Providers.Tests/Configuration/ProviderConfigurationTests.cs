using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Contracts.Dtos.Providers;
using Xunit;

namespace SimpleModelServer.Providers.Tests;

public class ProviderConfigurationTests
{
    [Fact]
    public void ProviderConfiguration_DefaultsToOllama()
    {
        var config = new ProviderConfiguration();

        Assert.Equal(Provider.Ollama, config.Type);
        Assert.Equal("127.0.0.1", config.Host);
        Assert.Equal(11434, config.Port);
    }

    [Fact]
    public void ProviderConfiguration_CanSetLMStudio()
    {
        var config = new ProviderConfiguration
        {
            Type = Provider.LMStudio,
            Host = "localhost",
            Port = 1234,
        };

        Assert.Equal(Provider.LMStudio, config.Type);
        Assert.Equal("localhost", config.Host);
        Assert.Equal(1234, config.Port);
    }

    [Fact]
    public void ProviderConfiguration_SupportsTimeout()
    {
        var config = new ProviderConfiguration
        {
            TimeoutSeconds = 30,
        };

        Assert.Equal(30, config.TimeoutSeconds);
    }

    [Fact]
    public void ProviderConfiguration_SupportsApiKey()
    {
        var config = new ProviderConfiguration
        {
            Type = Provider.LMStudio,
            ApiKey = "test-api-key-123",
        };

        Assert.Equal("test-api-key-123", config.ApiKey);
    }
}
