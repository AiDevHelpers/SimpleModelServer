using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;
using Xunit;

namespace SimpleModelServer.Config.Tests;

/// <summary>
/// Comprehensive tests for <see cref="IModelSourceConfig"/> interface and <see cref="ModelSourceConfig"/> implementation.
/// Validates model source configuration properties and their defaults.
/// </summary>
public class ModelSourceConfigTests
{
    /// <summary>
    /// Contract verification: IModelSourceConfig has all required members.
    /// </summary>
    [Fact]
    public void IModelSourceConfig_Interface_HasAllRequiredMembers()
    {
        // Arrange
        IModelSourceConfig config = new ModelSourceConfig();

        // Act & Assert - All properties must be accessible
        _ = config.Base;
        _ = config.NumCtx;
        _ = config.KeepAlive;
        _ = config.NumGpu;
        _ = config.Stop;
        _ = config.Family;
        _ = config.Variant;
        _ = config.Size;
        _ = config.ChatSuffix;
    }

    [Fact]
    public void ModelSourceConfig_Default_HasValidDefaults()
    {
        // Arrange & Act
        var config = new ModelSourceConfig();

        // Assert
        Assert.Equal("", config.Base);
        Assert.Null(config.NumCtx);
        Assert.Null(config.KeepAlive);
        Assert.Null(config.NumGpu);
        Assert.Empty(config.Stop);
        Assert.Null(config.Family);
        Assert.Null(config.Variant);
        Assert.Null(config.Size);
        Assert.Null(config.ChatSuffix);
    }

    [Theory]
    [InlineData("ollama:latest")]
    [InlineData("huggingface:model-name")]
    [InlineData("qwen2.5-coder-chat:0.5b")]
    [InlineData("")]
    public void Base_CanBeSet(string baseValue)
    {
        // Arrange & Act
        var config = new ModelSourceConfig { Base = baseValue };

        // Assert
        Assert.Equal(baseValue, config.Base);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2048)]
    [InlineData(4096)]
    [InlineData(8192)]
    public void NumCtx_CanBeSet(int? value)
    {
        // Arrange & Act
        var config = new ModelSourceConfig { NumCtx = value };

        // Assert
        Assert.Equal(value, config.NumCtx);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("5m")]
    [InlineData("10m")]
    [InlineData("1h")]
    [InlineData("")]
    public void KeepAlive_CanBeSet(string? value)
    {
        // Arrange & Act
        var config = new ModelSourceConfig { KeepAlive = value };

        // Assert
        Assert.Equal(value, config.KeepAlive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    public void NumGpu_CanBeSet(int? value)
    {
        // Arrange & Act
        var config = new ModelSourceConfig { NumGpu = value };

        // Assert
        Assert.Equal(value, config.NumGpu);
    }

    [Fact]
    public void Stop_IsReadOnly()
    {
        // Arrange
        var stopTokens = new[] { "</s>", "[END]", "[STOP]" };
        var config = new ModelSourceConfig { Stop = stopTokens };

        // Act
        IReadOnlyList<string> readOnly = config.Stop;

        // Assert
        Assert.NotNull(readOnly);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(readOnly);
        Assert.Equal(3, readOnly.Count);
        Assert.Contains("</s>", readOnly);
    }

    [Fact]
    public void Stop_DefaultIsEmpty()
    {
        // Arrange & Act
        var config = new ModelSourceConfig();

        // Assert
        Assert.NotNull(config.Stop);
        Assert.Empty(config.Stop);
    }

    [Fact]
    public void Stop_CanBeSetToMultipleTokens()
    {
        // Arrange
        var tokens = new[] { "token1", "token2", "token3" };

        // Act
        var config = new ModelSourceConfig { Stop = tokens };

        // Assert
        Assert.Equal(3, config.Stop.Count);
        foreach (string token in tokens)
        {
            Assert.Contains(token, config.Stop);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("qwen2.5")]
    [InlineData("deepseek")]
    [InlineData("mistral")]
    public void Family_CanBeSet(string? value)
    {
        // Arrange & Act
        var config = new ModelSourceConfig { Family = value };

        // Assert
        Assert.Equal(value, config.Family);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("coder")]
    [InlineData("instruct")]
    [InlineData("chat")]
    public void Variant_CanBeSet(string? value)
    {
        // Arrange & Act
        var config = new ModelSourceConfig { Variant = value };

        // Assert
        Assert.Equal(value, config.Variant);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.5b")]
    [InlineData("3b")]
    [InlineData("7b")]
    public void Size_CanBeSet(string? value)
    {
        // Arrange & Act
        var config = new ModelSourceConfig { Size = value };

        // Assert
        Assert.Equal(value, config.Size);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-chat")]
    [InlineData("-instruct")]
    [InlineData("")]
    public void ChatSuffix_CanBeSet(string? value)
    {
        // Arrange & Act
        var config = new ModelSourceConfig { ChatSuffix = value };

        // Assert
        Assert.Equal(value, config.ChatSuffix);
    }

    [Fact]
    public void ModelSourceConfig_CanBeCreatedWithAllProperties()
    {
        // Arrange & Act
        var config = new ModelSourceConfig
        {
            Base = "qwen2.5-coder-chat:0.5b",
            NumCtx = 4096,
            KeepAlive = "10m",
            NumGpu = 20,
            Stop = new[] { "</s>", "[STOP]" },
            Family = "qwen2.5",
            Variant = "coder",
            Size = "0.5b",
            ChatSuffix = "-chat"
        };

        // Assert
        Assert.Equal("qwen2.5-coder-chat:0.5b", config.Base);
        Assert.Equal(4096, config.NumCtx);
        Assert.Equal("10m", config.KeepAlive);
        Assert.Equal(20, config.NumGpu);
        Assert.Equal(2, config.Stop.Count);
        Assert.Equal("qwen2.5", config.Family);
        Assert.Equal("coder", config.Variant);
        Assert.Equal("0.5b", config.Size);
        Assert.Equal("-chat", config.ChatSuffix);
    }

    [Fact]
    public void ModelSourceConfig_CanBeUsedAsInterface()
    {
        // Arrange
        IModelSourceConfig config = new ModelSourceConfig
        {
            Base = "test-model:latest",
            NumCtx = 2048,
            KeepAlive = "5m"
        };

        // Act & Assert
        Assert.Equal("test-model:latest", config.Base);
        Assert.Equal(2048, config.NumCtx);
        Assert.Equal("5m", config.KeepAlive);
    }

    [Fact]
    public void ModelSourceConfig_NullProperties_CanBeReadWithoutError()
    {
        // Arrange
        var config = new ModelSourceConfig
        {
            Family = null,
            Variant = null,
            Size = null,
            KeepAlive = null
        };

        // Act & Assert - Should not throw
        _ = config.Family;
        _ = config.Variant;
        _ = config.Size;
        _ = config.KeepAlive;
        _ = config.NumGpu;
    }

    [Theory]
    [InlineData("", null, null, null, null)]
    [InlineData("base:latest", 2048, "5m", 10, "-chat")]
    [InlineData("other-base", 4096, null, null, "-instruct")]
    public void ModelSourceConfig_VariousConfigurations(
        string base_,
        int? numCtx,
        string? keepAlive,
        int? numGpu,
        string? chatSuffix)
    {
        // Arrange & Act
        var config = new ModelSourceConfig
        {
            Base = base_,
            NumCtx = numCtx,
            KeepAlive = keepAlive,
            NumGpu = numGpu,
            ChatSuffix = chatSuffix
        };

        // Assert
        Assert.Equal(base_, config.Base);
        Assert.Equal(numCtx, config.NumCtx);
        Assert.Equal(keepAlive, config.KeepAlive);
        Assert.Equal(numGpu, config.NumGpu);
        Assert.Equal(chatSuffix, config.ChatSuffix);
    }
}
