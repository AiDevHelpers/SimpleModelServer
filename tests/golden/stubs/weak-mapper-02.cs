// Stub for weak-mapper-02: the (deliberately terse/underspecified) prompt says "i have both classes
// already defined" for the domain Order and its DTO — give both minimal shapes so a mapper between
// them can compile.
namespace DraftModel.Evals.Stubs;

public sealed class Order
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
}

public sealed class OrderDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
}
