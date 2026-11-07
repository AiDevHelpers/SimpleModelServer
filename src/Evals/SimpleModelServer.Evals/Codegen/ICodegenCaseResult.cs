namespace SimpleModelServer.Evals.Codegen;

public interface ICodegenCaseResult
{
	string CaseId { get; }
	bool GenerationSucceeded { get; }
	string? GenerationError { get; }
	string ExtractedCode { get; }
	bool ParsePassed { get; }
	IReadOnlyList<IGateDiagnostic> ParseDiagnostics { get; }
	bool CompilePassed { get; }
	IReadOnlyList<IGateDiagnostic> CompileDiagnostics { get; }
	bool AssertionsPassed { get; }
	IReadOnlyList<IAssertionOutcome> Assertions { get; }
	double LatencyMs { get; }
	int? CompletionTokens { get; }
	string? FinishReason { get; }
	bool RepetitionLoopDetected { get; }
	bool FullyClean { get; }
}
