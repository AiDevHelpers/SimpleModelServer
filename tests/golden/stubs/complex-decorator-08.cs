// Stub for complex-decorator-08: IOrderRepository/Order are the "assume exist" dependencies being
// decorated. IMemoryCache is a real framework type (Microsoft.Extensions.Caching.Memory, part of
// Microsoft.AspNetCore.App) and needs no stub.
namespace DraftModel.Evals.Stubs;

public sealed class Order
{
    public Guid Id { get; set; }
}

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken);

    Task SaveAsync(Order order, CancellationToken cancellationToken);
}
