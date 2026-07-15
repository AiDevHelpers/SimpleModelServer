// Stub for complex-inventory-async-03: the prompt takes IStockRepository as a given injected
// dependency and expects InsufficientStockException to be thrown, but the model is meant to declare
// InventoryService/InventoryReservation itself.
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
