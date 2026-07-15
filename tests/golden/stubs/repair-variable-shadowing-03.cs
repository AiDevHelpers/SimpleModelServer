// Stub for repair-variable-shadowing-03: ProcessOrderBatches iterates IEnumerable<OrderBatch> and
// references a List<Discount> discounts field the prompt says exists on the containing class, plus
// an implied Order type inside each batch. None of these are meant to be declared by the model.
namespace DraftModel.Evals.Stubs;

public sealed class Discount
{
    public decimal Amount { get; set; }
}

public sealed class Order
{
    public Guid Id { get; set; }
    public decimal Total { get; set; }
}

public sealed class OrderBatch
{
    public IReadOnlyList<Order> Orders { get; set; } = [];
}
