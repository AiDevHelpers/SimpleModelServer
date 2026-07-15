namespace SimpleModelServer.Evals.Router;

public interface IRouterResponse
{
    string? Intent { get; }
    string? Quality { get; }
    string? Ambiguity { get; }
    double? Confidence { get; }
    string? RewrittenPrompt { get; }
    string? Plan { get; }
    string? Response { get; }
    string? RoutingReason { get; }
}
