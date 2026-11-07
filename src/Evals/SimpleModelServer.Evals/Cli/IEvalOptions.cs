namespace SimpleModelServer.Evals.Cli;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056",
	Justification = "RouterUrl/GenUrl are plain strings parsed from CLI args and used directly in string interpolation/HTTP calls; changing to System.Uri would complicate call sites for no functional benefit.")]
public interface IEvalOptions
{
	EvalMode Mode { get; }
	string? RouterUrl { get; }
	string? GenUrl { get; }
	string Label { get; }
	string CasesDir { get; }
	string? Filter { get; }
	int NPredict { get; }
	double Temperature { get; }
	int TimeoutSeconds { get; }
	string ReportsDir { get; }
}
