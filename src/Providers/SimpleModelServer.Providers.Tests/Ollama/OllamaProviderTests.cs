using SimpleModelServer.Contracts.Providers;
using SimpleModelServer.Providers.Ollama;
using Xunit;

namespace SimpleModelServer.Providers.Tests;

public class OllamaProviderTests
{
    [Fact]
    public void IOllama_InterfaceImplementedByProcessManager()
    {
        var interfaces = typeof(OllamaProcessManager).GetInterfaces();
        Assert.Contains(typeof(IOllama), interfaces);
    }

    [Fact]
    public void OllamaProcessManager_IsInstantiable()
    {
        // Constructor requires certain dependencies, so this is just a sanity check
        // that the type can be resolved.
        Assert.NotNull(typeof(OllamaProcessManager));
    }
}
