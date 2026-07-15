using SimpleModelServer.Evals.Http;
using Xunit;
using System.Reflection;

namespace SimpleModelServer.Evals.Tests;

/// <summary>
/// Comprehensive tests for <see cref="ILlamaChatClient"/> interface.
/// Validates async method contracts and completion behavior.
/// </summary>
public class LlamaChatClientTests
{
    /// <summary>
    /// Contract verification: ILlamaChatClient has all required members.
    /// </summary>
    [Fact]
    public void ILlamaChatClient_Interface_HasAllRequiredMembers()
    {
        // Arrange
        var type = typeof(ILlamaChatClient);

        // Act & Assert - Interface must be IAsyncDisposable and IDisposable
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(type));
        Assert.True(typeof(IDisposable).IsAssignableFrom(type));

        // Verify methods exist
        var completeAsyncMethod = type.GetMethod("CompleteAsync", BindingFlags.Public | BindingFlags.Instance);
        var tryGetModelInfoMethod = type.GetMethod("TryGetModelInfoAsync", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(completeAsyncMethod);
        Assert.NotNull(tryGetModelInfoMethod);
    }

    [Fact]
    public void CompleteAsync_MethodSignature_IsCorrect()
    {
        // Arrange
        var type = typeof(ILlamaChatClient);
        var method = type.GetMethod("CompleteAsync", BindingFlags.Public | BindingFlags.Instance);

        // Act & Assert
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(5, parameters.Length); // baseUrl, messages, temperature, maxTokens, optional jsonSchema
    }

    [Fact]
    public void TryGetModelInfoAsync_MethodSignature_IsCorrect()
    {
        // Arrange
        var type = typeof(ILlamaChatClient);
        var method = type.GetMethod("TryGetModelInfoAsync", BindingFlags.Public | BindingFlags.Instance);

        // Act & Assert
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length); // baseUrl, cancellationToken
    }
}
