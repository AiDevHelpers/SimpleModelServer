using SimpleModelServer.Discovery;

namespace SimpleModelServer.UI.Core
{
    /// <summary>
    /// Adapter that implements IServerConnectionStore by mapping between ServerConnectionSettings
    /// and SettingsManager's properties.
    /// </summary>
    public class SettingsManagerConnectionStore : IServerConnectionStore
    {
        private readonly SettingsManager _settingsManager;

        /// <summary>
        /// Creates a new SettingsManagerConnectionStore.
        /// </summary>
        /// <param name="settingsManager">The SettingsManager instance to adapt.</param>
        public SettingsManagerConnectionStore(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        /// <inheritdoc />
        public async Task<ServerConnectionSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            // Ensure settings are loaded from disk
            await _settingsManager.LoadAsync(cancellationToken);

            return new ServerConnectionSettings
            {
                Mode = _settingsManager.ConnectionMode,
                ManualHost = _settingsManager.ServerHost,
                ManualPort = _settingsManager.ServerPort,
                RememberedMdnsInstanceName = _settingsManager.RememberedMdnsInstanceName,
                ApiKey = _settingsManager.ApiKey,
            };
        }

        /// <inheritdoc />
        public async Task SaveAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);

            _settingsManager.ConnectionMode = settings.Mode;
            _settingsManager.ServerHost = settings.ManualHost ?? "localhost";
            _settingsManager.ServerPort = settings.ManualPort > 0 ? settings.ManualPort : 11435;
            _settingsManager.RememberedMdnsInstanceName = settings.RememberedMdnsInstanceName;
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                _settingsManager.ApiKey = settings.ApiKey;
            }

            await _settingsManager.SaveAsync(cancellationToken);
        }
    }
}
