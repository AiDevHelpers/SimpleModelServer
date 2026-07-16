using SimpleModelServer.UI.Core;
using SimpleModelServer.Discovery;
using Xunit;
using System.IO;

namespace SimpleModelServer.UI.Tests.Core
{
    public class SettingsManagerConnectionStoreTests : IDisposable
    {
        private readonly string _tempSettingsFile;

        public SettingsManagerConnectionStoreTests()
        {
            _tempSettingsFile = Path.Combine(Path.GetTempPath(), $"test-store-{Guid.NewGuid()}.json");
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
        public void Constructor_RequiresSettingsManager()
        {
            Assert.Throws<ArgumentNullException>(() => new SettingsManagerConnectionStore(null!));
        }

        [Fact]
        public async Task LoadAsync_ReturnsServerConnectionSettings()
        {
            var manager = new SettingsManager(_tempSettingsFile);
            manager.ConnectionMode = ConnectionMode.Manual;
            manager.ServerHost = "test.example.com";
            manager.ServerPort = 9999;
            manager.ApiKey = "test-api-key";
            manager.RememberedMdnsInstanceName = "my-instance";

            var store = new SettingsManagerConnectionStore(manager);
            var settings = await store.LoadAsync();

            Assert.NotNull(settings);
            Assert.Equal(ConnectionMode.Manual, settings.Mode);
            Assert.Equal("test.example.com", settings.ManualHost);
            Assert.Equal(9999, settings.ManualPort);
            Assert.Equal("test-api-key", settings.ApiKey);
            Assert.Equal("my-instance", settings.RememberedMdnsInstanceName);

            manager.Dispose();
        }

        [Fact]
        public async Task SaveAsync_UpdatesSettingsManager()
        {
            var manager = new SettingsManager(_tempSettingsFile);
            var store = new SettingsManagerConnectionStore(manager);

            var newSettings = new ServerConnectionSettings
            {
                Mode = ConnectionMode.Mdns,
                ManualHost = "192.168.1.100",
                ManualPort = 11435,
                ApiKey = "new-api-key",
                RememberedMdnsInstanceName = "discovered-instance"
            };

            await store.SaveAsync(newSettings);

            // Verify the settings manager was updated
            Assert.Equal(ConnectionMode.Mdns, manager.ConnectionMode);
            Assert.Equal("192.168.1.100", manager.ServerHost);
            Assert.Equal(11435, manager.ServerPort);
            Assert.Equal("new-api-key", manager.ApiKey);
            Assert.Equal("discovered-instance", manager.RememberedMdnsInstanceName);

            manager.Dispose();
        }

        [Fact]
        public async Task SaveAsync_PersistsToFile()
        {
            var manager = new SettingsManager(_tempSettingsFile);
            var store = new SettingsManagerConnectionStore(manager);

            var settings = new ServerConnectionSettings
            {
                Mode = ConnectionMode.Local,
                ManualHost = "localhost",
                ManualPort = 11435,
                ApiKey = "local-key",
                RememberedMdnsInstanceName = null
            };

            await store.SaveAsync(settings);
            await manager.SaveAsync();

            Assert.True(File.Exists(_tempSettingsFile));
            var content = await File.ReadAllTextAsync(_tempSettingsFile);
            Assert.Contains("Local", content);
            Assert.Contains("localhost", content);

            manager.Dispose();
        }

        [Fact]
        public async Task SaveAsync_RequiresNonNullSettings()
        {
            var manager = new SettingsManager(_tempSettingsFile);
            var store = new SettingsManagerConnectionStore(manager);

            await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(null!));

            manager.Dispose();
        }
    }
}
