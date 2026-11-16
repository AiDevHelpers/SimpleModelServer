namespace SimpleModelServer.Evals.Router;

public sealed record RouterCaseResult(
	string CaseId,
	string ExpectedIntent,
	string? ActualIntent,
	string ExpectedQuality,
	string? ActualQuality,
	string ExpectedAmbiguity,
	string? ActualAmbiguity,
	string ExpectedRoutingReason,
	string? ActualRoutingReason,
	bool SchemaValid,
	double LatencyMs,
	string? Error) : IRouterCaseResult
{
	public bool IntentMatch => SchemaValid && string.Equals(ExpectedIntent, ActualIntent, StringComparison.OrdinalIgnoreCase);
	public bool QualityMatch => SchemaValid && string.Equals(ExpectedQuality, ActualQuality, StringComparison.OrdinalIgnoreCase);
	public bool AmbiguityMatch => SchemaValid && string.Equals(ExpectedAmbiguity, ActualAmbiguity, StringComparison.OrdinalIgnoreCase);
	public bool RoutingReasonMatch => SchemaValid && string.Equals(ExpectedRoutingReason, ActualRoutingReason, StringComparison.OrdinalIgnoreCase);
}

public sealed record RouterSummary(
	int TotalCases,
	int SchemaValidCount,
	double IntentAccuracyPct,
	double RoutingReasonAccuracyPct,
	IReadOnlyDictionary<string, int> IntentConfusion,
	IReadOnlyDictionary<string, int> RoutingReasonConfusion,
	double P50LatencyMs,
	double P95LatencyMs) : IRouterSummary;
