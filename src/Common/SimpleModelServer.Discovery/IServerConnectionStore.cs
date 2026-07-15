namespace SimpleModelServer.Discovery;

/// <summary>
/// Interface for the caller-supplied persistence backend that stores and retrieves server connection settings.
/// </summary>
public interface IServerConnectionStore
{
    /// <summary>
    /// Asynchronously loads the server connection settings.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The loaded server connection settings.</returns>
    Task<ServerConnectionSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously saves the server connection settings.
    /// </summary>
    /// <param name="settings">The settings to save.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default);
}
