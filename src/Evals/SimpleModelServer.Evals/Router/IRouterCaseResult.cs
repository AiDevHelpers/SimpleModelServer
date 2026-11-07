namespace SimpleModelServer.Evals.Router;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716",
	Justification = "Error is domain vocabulary consistent with the rest of this result-DTO family (IChatCompletionResult, IGenerationOutcome); renaming for cross-language interop that doesn't apply here would ripple across many call sites for no functional benefit.")]
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
