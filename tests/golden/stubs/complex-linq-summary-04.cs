// Stub for complex-linq-summary-04: SummarizeTopCustomers takes IEnumerable<Order>; Order itself
// isn't declared in the prompt, so give it just enough shape (CustomerId, spend amount) for the
// grouping/aggregation logic to compile. CustomerSpendSummary is left for the model to declare.
namespace DraftModel.Evals.Stubs;

public sealed class Order
{
    public Guid CustomerId { get; set; }

    public decimal Total { get; set; }
}
