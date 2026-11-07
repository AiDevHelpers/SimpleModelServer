using SimpleModelServer.UI.Core;
using SimpleModelServer.Discovery;
using Xunit;
using System.IO;

namespace SimpleModelServer.UI.Tests.Core
{
	public class SettingsManagerPersistenceTests : IDisposable
	{
		private readonly string _tempSettingsFile;

		public SettingsManagerPersistenceTests()
		{
			_tempSettingsFile = Path.Combine(Path.GetTempPath(), $"test-settings-{Guid.NewGuid()}.json");
		}

		public void Dispose()
		{
			if (File.Exists(_tempSettingsFile))
			{
				try
				{
					File.Delete(_tempSettingsFile);
				}
				catch
				{
					// Ignore cleanup errors in tests
				}
			}
		}

		[Fact]
		public async Task SaveAsync_PersistsSettingsToFile()
		{
			var manager = new SettingsManager(_tempSettingsFile);
			manager.ServerHost = "example.com";
			manager.ServerPort = 9999;
			manager.ApiKey = "test-key-123";
			manager.ConnectionMode = ConnectionMode.Manual;

			await manager.SaveAsync();

			Assert.True(File.Exists(_tempSettingsFile));
			var content = await File.ReadAllTextAsync(_tempSettingsFile);
			Assert.Contains("example.com", content);
			Assert.Contains("9999", content);
			Assert.Contains("test-key-123", content);
			Assert.Contains("Manual", content);
		}

		[Fact]
		public async Task LoadAsync_RestoresSettingsFromFile()
		{
			// First manager: save settings
			var manager1 = new SettingsManager(_tempSettingsFile);
			manager1.ServerHost = "192.168.1.1";
			manager1.ServerPort = 8080;
			manager1.ApiKey = "saved-key";
			manager1.ConnectionMode = ConnectionMode.Mdns;
			manager1.RememberedMdnsInstanceName = "my-instance";
			await manager1.SaveAsync();
			manager1.Dispose();

			// Second manager: load settings
			var manager2 = new SettingsManager(_tempSettingsFile);
			await manager2.LoadAsync();

			Assert.Equal("192.168.1.1", manager2.ServerHost);
			Assert.Equal(8080, manager2.ServerPort);
			Assert.Equal("saved-key", manager2.ApiKey);
			Assert.Equal(ConnectionMode.Mdns, manager2.ConnectionMode);
			Assert.Equal("my-instance", manager2.RememberedMdnsInstanceName);
			manager2.Dispose();
		}

		[Fact]
		public async Task ConnectionMode_CanBeSet()
		{
			var manager = new SettingsManager(_tempSettingsFile);

			manager.ConnectionMode = ConnectionMode.Local;
			Assert.Equal(ConnectionMode.Local, manager.ConnectionMode);

			manager.ConnectionMode = ConnectionMode.Mdns;
			Assert.Equal(ConnectionMode.Mdns, manager.ConnectionMode);

			manager.ConnectionMode = ConnectionMode.Manual;
			Assert.Equal(ConnectionMode.Manual, manager.ConnectionMode);

			manager.Dispose();
		}

		[Fact]
		public async Task RememberedMdnsInstanceName_CanBeSetAndGet()
		{
			var manager = new SettingsManager(_tempSettingsFile);

			manager.RememberedMdnsInstanceName = "test-instance";
			Assert.Equal("test-instance", manager.RememberedMdnsInstanceName);

			// Setting to null converts to empty string internally
			manager.RememberedMdnsInstanceName = null;
			Assert.Equal(string.Empty, manager.RememberedMdnsInstanceName);

			manager.Dispose();
		}
	}
}
