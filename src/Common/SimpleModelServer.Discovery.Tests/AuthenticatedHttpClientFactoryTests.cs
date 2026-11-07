using System.Net.Http.Headers;
using Xunit;

namespace SimpleModelServer.Discovery.Tests;

public sealed class AuthenticatedHttpClientFactoryTests
{
	[Fact]
	public void Create_WithNonLoopbackAndApiKey_SetsAuthorizationHeader()
	{
		// Arrange
		var connection = new ResolvedServerConnection
		{
			BaseUrl = "http://192.168.1.5:11435",
			IsLoopback = false,
			ApiKey = "test-api-key-123",
			ResolvedVia = ConnectionMode.Mdns,
		};

		// Act
		var client = AuthenticatedHttpClientFactory.Create(connection);

		// Assert
		Assert.NotNull(client.DefaultRequestHeaders.Authorization);
		Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization.Scheme);
		Assert.Equal("test-api-key-123", client.DefaultRequestHeaders.Authorization.Parameter);
		Assert.Equal(new Uri("http://192.168.1.5:11435"), client.BaseAddress);
	}

	[Fact]
	public void Create_WithLoopbackAndApiKey_DoesNotSetAuthorizationHeader()
	{
		// Arrange
		var connection = new ResolvedServerConnection
		{
			BaseUrl = "http://localhost:11435",
			IsLoopback = true,
			ApiKey = "test-api-key-123",
			ResolvedVia = ConnectionMode.Local,
		};

		// Act
		var client = AuthenticatedHttpClientFactory.Create(connection);

		// Assert
		Assert.Null(client.DefaultRequestHeaders.Authorization);
		Assert.Equal(new Uri("http://localhost:11435"), client.BaseAddress);
	}

	[Fact]
	public void Create_WithLoopbackAndNoApiKey_DoesNotSetAuthorizationHeader()
	{
		// Arrange
		var connection = new ResolvedServerConnection
		{
			BaseUrl = "http://localhost:11435",
			IsLoopback = true,
			ApiKey = null,
			ResolvedVia = ConnectionMode.Local,
		};

		// Act
		var client = AuthenticatedHttpClientFactory.Create(connection);

		// Assert
		Assert.Null(client.DefaultRequestHeaders.Authorization);
	}

	[Fact]
	public void Create_WithNonLoopbackAndNoApiKey_DoesNotSetAuthorizationHeader()
	{
		// Arrange
		var connection = new ResolvedServerConnection
		{
			BaseUrl = "http://192.168.1.5:11435",
			IsLoopback = false,
			ApiKey = null,
			ResolvedVia = ConnectionMode.Mdns,
		};

		// Act
		var client = AuthenticatedHttpClientFactory.Create(connection);

		// Assert
		Assert.Null(client.DefaultRequestHeaders.Authorization);
	}

	[Fact]
	public void Create_WithNonLoopbackAndWhitespaceApiKey_DoesNotSetAuthorizationHeader()
	{
		// Arrange
		var connection = new ResolvedServerConnection
		{
			BaseUrl = "http://192.168.1.5:11435",
			IsLoopback = false,
			ApiKey = "   ",
			ResolvedVia = ConnectionMode.Mdns,
		};

		// Act
		var client = AuthenticatedHttpClientFactory.Create(connection);

		// Assert
		Assert.Null(client.DefaultRequestHeaders.Authorization);
	}

	[Fact]
	public void Create_ThrowsOnNullConnection()
	{
		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => AuthenticatedHttpClientFactory.Create(null!));
	}
}
