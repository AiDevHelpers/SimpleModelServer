namespace SimpleModelServer.Evals.Router;

public interface IRouterSummary
{
    int TotalCases { get; }
    int SchemaValidCount { get; }
    double IntentAccuracyPct { get; }
    double RoutingReasonAccuracyPct { get; }
    IReadOnlyDictionary<string, int> IntentConfusion { get; }
    IReadOnlyDictionary<string, int> RoutingReasonConfusion { get; }
    double P50LatencyMs { get; }
    double P95LatencyMs { get; }
}
