using SimpleModelServer.UI.Core;
using Xunit;

namespace SimpleModelServer.UI.Tests.Core
{
	public class SettingsManagerTests
	{
		[Fact]
		public void Constructor_LoadsDefaults()
		{
			var manager = new SettingsManager();

			Assert.Equal("localhost", manager.ServerHost);
			Assert.Equal(11434, manager.ServerPort);
			Assert.Equal(string.Empty, manager.ApiKey);
			Assert.Equal("Ask", manager.DefaultAgentType);
			Assert.Equal("all", manager.EnabledFeatures);
			Assert.True(manager.RememberLastChat);
		}

		[Fact]
		public void ServerHost_CanSetAndGet()
		{
			var manager = new SettingsManager();
			manager.ServerHost = "127.0.0.1";

			Assert.Equal("127.0.0.1", manager.ServerHost);
		}

		[Fact]
		public void ServerPort_CanSetAndGet()
		{
			var manager = new SettingsManager();
			manager.ServerPort = 9999;

			Assert.Equal(9999, manager.ServerPort);
		}

		[Fact]
		public void ApiKey_CanSetAndGet()
		{
			var manager = new SettingsManager();
			manager.ApiKey = "test-key-123";

			Assert.Equal("test-key-123", manager.ApiKey);
		}

		[Fact]
		public void DefaultAgentType_CanSetAndGet()
		{
			var manager = new SettingsManager();
			manager.DefaultAgentType = "Code";

			Assert.Equal("Code", manager.DefaultAgentType);
		}

		[Fact]
		public void RememberLastChat_CanToggle()
		{
			var manager = new SettingsManager();
			Assert.True(manager.RememberLastChat);

			manager.RememberLastChat = false;
			Assert.False(manager.RememberLastChat);
		}

		[Fact]
		public void GetServerUrl_ReturnsCorrectFormat()
		{
			var manager = new SettingsManager();
			manager.ServerHost = "example.com";
			manager.ServerPort = 8080;

			Assert.Equal("http://example.com:8080", manager.GetServerUrl());
		}

		[Fact]
		public void GetChatEndpoint_ReturnsCorrectEndpoint()
		{
			var manager = new SettingsManager();
			manager.ServerHost = "localhost";
			manager.ServerPort = 11435;

			Assert.Equal("http://localhost:11435/api/generate", manager.GetChatEndpoint());
		}

		[Fact]
		public void ChatHistory_CanSetAndGet()
		{
			var manager = new SettingsManager();
			var history = "message1\nmessage2";
			manager.ChatHistory = history;

			Assert.Equal(history, manager.ChatHistory);
		}

		[Fact]
		public void EnabledFeatures_CanSetAndGet()
		{
			var manager = new SettingsManager();
			manager.EnabledFeatures = "explain,generate";

			Assert.Equal("explain,generate", manager.EnabledFeatures);
		}
	}
}
