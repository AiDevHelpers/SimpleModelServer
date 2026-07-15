// Stub for complex-pricing-service-02: the prompt says "Assume Order, ITaxRateProvider,
// IDiscountRepository already exist with reasonable members" — these are the pre-existing types,
// distinct from OrderTotal, which the prompt asks the model to declare itself.
namespace DraftModel.Evals.Stubs;

public sealed class OrderLineItem
{
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public sealed class Order
{
    public System.Guid OrderId { get; set; }
    public System.Guid CustomerId { get; set; }
    public string Region { get; set; } = "";
    public System.Collections.Generic.IReadOnlyList<OrderLineItem> LineItems { get; set; } = [];
}

public sealed class Discount
{
    public decimal Percentage { get; set; }
}

public interface ITaxRateProvider
{
    decimal GetTaxRate(string region);
}

public interface IDiscountRepository
{
    Discount? GetActiveDiscount(System.Guid customerId);
}
