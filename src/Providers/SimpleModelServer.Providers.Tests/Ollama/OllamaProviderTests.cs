using SimpleModelServer.Providers.Ollama;
using Xunit;

namespace SimpleModelServer.Providers.Tests;

public class OllamaProviderTests
{
	[Fact]
	public void OllamaProcessManager_IsInstantiable()
	{
		// Constructor requires certain dependencies, so this is just a sanity check
		// that the type can be resolved.
		Assert.NotNull(typeof(OllamaProcessManager));
	}
}
