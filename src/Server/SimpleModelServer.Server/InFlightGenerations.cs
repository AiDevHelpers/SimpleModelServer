using System.Collections.Concurrent;

namespace SimpleModelServer.Server;

/// <summary>
///     Counts the generations the router has running per model. A model with a generation in flight
///     is demonstrably serving, so readiness probes must not queue behind it and misreport it as
///     unresponsive (see the warm-up gate and the periodic health check).
/// </summary>
internal sealed class InFlightGenerations
{
    private readonly ConcurrentDictionary<string, int> counts = new(StringComparer.Ordinal);

    /// <summary>Marks a generation on <paramref name="model" /> as running until the returned token is disposed.</summary>
    public IDisposable Track(string model)
    {
        _ = counts.AddOrUpdate(model, 1, (_, n) => n + 1);
        return new Release(this, model);
    }

    public bool IsBusy(string model) => counts.TryGetValue(model, out int n) && n > 0;

    private void Decrement(string model) => _ = counts.AddOrUpdate(model, 0, (_, n) => Math.Max(0, n - 1));

    private sealed class Release(InFlightGenerations owner, string model) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Decrement(model);
            }
        }
    }
}
