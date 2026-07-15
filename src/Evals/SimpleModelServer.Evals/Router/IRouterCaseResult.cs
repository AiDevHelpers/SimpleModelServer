namespace SimpleModelServer.Evals.Router;

public interface IRouterCaseResult
{
    string CaseId { get; }
    string ExpectedIntent { get; }
    string? ActualIntent { get; }
    string ExpectedQuality { get; }
    string? ActualQuality { get; }
    string ExpectedAmbiguity { get; }
    string? ActualAmbiguity { get; }
    string ExpectedRoutingReason { get; }
    string? ActualRoutingReason { get; }
    bool SchemaValid { get; }
    double LatencyMs { get; }
    string? Error { get; }
    bool IntentMatch { get; }
    bool QualityMatch { get; }
    bool AmbiguityMatch { get; }
    bool RoutingReasonMatch { get; }
}
