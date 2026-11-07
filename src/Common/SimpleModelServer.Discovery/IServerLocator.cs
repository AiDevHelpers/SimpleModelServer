namespace SimpleModelServer.Discovery;

/// <summary>
/// Interface for locating and resolving SimpleModelServer instances.
/// </summary>
public interface IServerLocator
{
	/// <summary>
	/// Asynchronously resolves the server connection, using cached result if available.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A resolved server connection.</returns>
	Task<ResolvedServerConnection> ResolveAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Asynchronously rediscovers the server connection, clearing any cached result.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A resolved server connection.</returns>
	Task<ResolvedServerConnection> RediscoverAsync(CancellationToken cancellationToken = default);
}
