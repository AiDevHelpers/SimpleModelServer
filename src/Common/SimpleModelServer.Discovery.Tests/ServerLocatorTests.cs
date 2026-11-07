using Moq;
using Xunit;

namespace SimpleModelServer.Discovery.Tests;

public sealed class ServerLocatorTests
{
	[Fact]
	public async Task ResolveAsync_Manual_BuildsBaseUrlAndPreservesApiKey()
	{
		// Arrange
		var settings = new ServerConnectionSettings
		{
			Mode = ConnectionMode.Manual,
			ManualHost = "example.com",
			ManualPort = 8080,
			ApiKey = "test-key-123",
		};

		var mockStore = new Mock<IServerConnectionStore>();
		mockStore
			.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(settings);

		var locator = new ServerLocator(mockStore.Object);

		// Act
		var result = await locator.ResolveAsync();

		// Assert
		Assert.Equal("http://example.com:8080", result.BaseUrl);
		Assert.Equal("test-key-123", result.ApiKey);
		Assert.False(result.IsLoopback);
		Assert.Equal(ConnectionMode.Manual, result.ResolvedVia);
	}

	[Fact]
	public async Task ResolveAsync_Manual_WithLocalhostHost_SetsIsLoopbackTrueAndNullApiKey()
	{
		// Arrange
		var settings = new ServerConnectionSettings
		{
			Mode = ConnectionMode.Manual,
			ManualHost = "localhost",
			ManualPort = 11435,
			ApiKey = "test-key-123",
		};

		var mockStore = new Mock<IServerConnectionStore>();
		mockStore
			.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(settings);

		var locator = new ServerLocator(mockStore.Object);

		// Act
		var result = await locator.ResolveAsync();

		// Assert
		Assert.Equal("http://localhost:11435", result.BaseUrl);
		Assert.Null(result.ApiKey);
		Assert.True(result.IsLoopback);
		Assert.Equal(ConnectionMode.Manual, result.ResolvedVia);
	}

	[Fact]
	public async Task ResolveAsync_Manual_WithLoopbackIpAddress_SetsIsLoopbackTrueAndNullApiKey()
	{
		// Arrange
		var settings = new ServerConnectionSettings
		{
			Mode = ConnectionMode.Manual,
			ManualHost = "127.0.0.1",
			ManualPort = 11435,
			ApiKey = "test-key-123",
		};

		var mockStore = new Mock<IServerConnectionStore>();
		mockStore
			.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(settings);

		var locator = new ServerLocator(mockStore.Object);

		// Act
		var result = await locator.ResolveAsync();

		// Assert
		Assert.True(result.IsLoopback);
		Assert.Null(result.ApiKey);
	}

	[Fact]
	public async Task ResolveAsync_Manual_WithIpv6Loopback_SetsIsLoopbackTrueAndNullApiKey()
	{
		// Arrange
		var settings = new ServerConnectionSettings
		{
			Mode = ConnectionMode.Manual,
			ManualHost = "::1",
			ManualPort = 11435,
			ApiKey = "test-key-123",
		};

		var mockStore = new Mock<IServerConnectionStore>();
		mockStore
			.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(settings);

		var locator = new ServerLocator(mockStore.Object);

		// Act
		var result = await locator.ResolveAsync();

		// Assert
		Assert.True(result.IsLoopback);
		Assert.Null(result.ApiKey);
	}

	[Fact]
	public async Task ResolveAsync_CachesResult()
	{
		// Arrange
		var settings = new ServerConnectionSettings
		{
			Mode = ConnectionMode.Manual,
			ManualHost = "example.com",
			ManualPort = 8080,
			ApiKey = "test-key",
		};

		var mockStore = new Mock<IServerConnectionStore>();
		mockStore
			.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(settings);

		var locator = new ServerLocator(mockStore.Object);

		// Act - call ResolveAsync twice
		var result1 = await locator.ResolveAsync();
		var result2 = await locator.ResolveAsync();

		// Assert - should be the same cached instance
		Assert.Same(result1, result2);

		// Store should only be called once (cached second time)
		mockStore.Verify(
			s => s.LoadAsync(It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public async Task RediscoverAsync_ClearsCache()
	{
		// Arrange
		var settings = new ServerConnectionSettings
		{
			Mode = ConnectionMode.Manual,
			ManualHost = "example.com",
			ManualPort = 8080,
			ApiKey = "test-key",
		};

		var mockStore = new Mock<IServerConnectionStore>();
		mockStore
			.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(settings);

		var locator = new ServerLocator(mockStore.Object);

		// Act
		var result1 = await locator.ResolveAsync();
		var result2 = await locator.RediscoverAsync();

		// Assert - should be different instances after rediscovery
		Assert.NotSame(result1, result2);

		// Store should be called twice (once for resolve, once for rediscover)
		mockStore.Verify(
			s => s.LoadAsync(It.IsAny<CancellationToken>()),
			Times.Exactly(2));
	}
}
