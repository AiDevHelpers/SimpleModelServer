using System.Net;

using Xunit;

namespace SimpleModelServer.Server.Tests;

using SimpleModelServer.Providers.Ollama;

/// <summary>
///     Covers OllamaProcessManager's two static, dependency-free helpers:
///     <see cref="OllamaProcessManager.IsAddressInUse" /> (classifying an HttpListenerException as
///     "port already bound" vs. some other failure — this decides whether SimpleModelPairApp.RunAsync
///     retries with a kill+restart or gives up) and <see cref="OllamaProcessManager.CommandExists" />
///     (the PATH lookup that decides whether "ollama" is considered installed). Neither had any
///     direct test before — both were only ever exercised indirectly (or not at all) through
///     RouterTestFixture, which never triggers a real port conflict or a missing/present PATH
///     command.
/// </summary>
public sealed class OllamaProcessManagerStaticTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(183)]
    public void IsAddressInUse_KnownWindowsErrorCodes_ReturnsTrue(int errorCode)
    {
        HttpListenerException ex = new(errorCode, "some message");
        Assert.True(OllamaProcessManager.IsAddressInUse(ex));
    }

    [Theory]
    [InlineData("The process cannot access the file because it conflicts with an existing registration on the machine.")]
    [InlineData("Address already in use")]
    [InlineData("that port is already in use elsewhere")]
    public void IsAddressInUse_KnownMessageSubstrings_ReturnsTrue(string message)
    {
        // ErrorCode 0 (not 32/183) — only the message text should trigger the match here, so
        // this also proves the message check isn't just riding along with the error-code check.
        HttpListenerException ex = new(0, message);
        Assert.True(OllamaProcessManager.IsAddressInUse(ex));
    }

    [Fact]
    public void IsAddressInUse_MessageMatch_IsCaseInsensitive()
    {
        HttpListenerException ex = new(0, "ADDRESS ALREADY IN USE");
        Assert.True(OllamaProcessManager.IsAddressInUse(ex));
    }

    [Fact]
    public void IsAddressInUse_UnrelatedError_ReturnsFalse()
    {
        HttpListenerException ex = new(5, "Access is denied.");
        Assert.False(OllamaProcessManager.IsAddressInUse(ex));
    }

    [Fact]
    public void CommandExists_WellKnownWindowsCommand_ReturnsTrue()
    {
        // "cmd" resolves via PATHEXT (.EXE) on any Windows box these tests run on — this is the
        // real production code path (PATH + PATHEXT scan via File.Exists), not a stub.
        Assert.True(OllamaProcessManager.CommandExists("cmd"));
    }

    [Fact]
    public void CommandExists_NonexistentCommand_ReturnsFalse()
    {
        Assert.False(OllamaProcessManager.CommandExists("definitely-not-a-real-command-xyz-123"));
    }
}


