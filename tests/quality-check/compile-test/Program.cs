using System;
using System.Collections.Generic;
using System.Linq;

public class CustomerSpendSummary
{
    public int CustomerId { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalSpend { get; set; }
    public decimal AverageOrderValue { get; set; }
}

public class Order
{
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
}

public class ProgramX
{
    public static List<CustomerSpendSummary> SummarizeTopCustomers(IEnumerable<Order> orders, int topN)
    {
        return orders
            .GroupBy(order => order.CustomerId)
            .Select(group => new CustomerSpendSummary
            {
                CustomerId = group.Key,
                OrderCount = group.Count(),
                TotalSpend = group.Sum(order => order.Amount),
                AverageOrderValue = group.Count() > 0 ? group.Average(order => order.Amount) : 0
            })
            .Where(customer => customer.OrderCount >= 2)
            .OrderByDescending(customer => customer.TotalSpend)
            .Take(topN)
            .ToList();
    }
}

class Program { static void Main() { Console.WriteLine("ok"); } }
