// Same assumed dependencies as complex-inventory-async-03 (this case is the repair-loop variant of
// the same prompt, guarding the EF Core DbContext hallucination Phase 0 found).
namespace DraftModel.Evals.Stubs;

public interface IStockRepository
{
    Task<int> GetAvailableStockAsync(Guid productId, CancellationToken cancellationToken);

    Task DecrementStockAsync(Guid productId, int quantity, CancellationToken cancellationToken);
}

public sealed class InsufficientStockException : Exception
{
    public InsufficientStockException()
    {
    }

    public InsufficientStockException(string message) : base(message)
    {
    }
}
