namespace SimpleModelServer.Evals.Models;

public interface IExpectedResult
{
	string Intent { get; }
	string Quality { get; }
	string Ambiguity { get; }
	string RoutingReason { get; }
	bool MustCompile { get; }
	string LatencyClass { get; }
	IReadOnlyList<string>? MustContain { get; }
	IReadOnlyList<string>? MustNotContain { get; }
	IReadOnlyList<string> MustContainSafe { get; }
	IReadOnlyList<string> MustNotContainSafe { get; }
}
