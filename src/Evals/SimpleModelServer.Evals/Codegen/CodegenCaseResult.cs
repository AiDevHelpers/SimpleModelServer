namespace SimpleModelServer.Evals.Codegen;

public sealed record CodegenCaseResult(
	string CaseId,
	bool GenerationSucceeded,
	string? GenerationError,
	string ExtractedCode,
	bool ParsePassed,
	IReadOnlyList<GateDiagnostic> ParseDiagnostics,
	bool CompilePassed,
	IReadOnlyList<GateDiagnostic> CompileDiagnostics,
	bool AssertionsPassed,
	IReadOnlyList<AssertionOutcome> Assertions,
	double LatencyMs,
	int? CompletionTokens,
	string? FinishReason,
	bool RepetitionLoopDetected) : ICodegenCaseResult
{
	/// <summary>All three gates green — the harness's per-case "fully clean" flag.</summary>
	public bool FullyClean => GenerationSucceeded && ParsePassed && CompilePassed && AssertionsPassed;

	IReadOnlyList<IGateDiagnostic> ICodegenCaseResult.ParseDiagnostics => ParseDiagnostics;
	IReadOnlyList<IGateDiagnostic> ICodegenCaseResult.CompileDiagnostics => CompileDiagnostics;
	IReadOnlyList<IAssertionOutcome> ICodegenCaseResult.Assertions => Assertions;
}

public sealed record CodegenSummary(
	int TotalCases,
	double ParsePassRatePct,
	double CompilePassRatePct,
	double AssertionPassRatePct,
	double FullyCleanRatePct,
	int RepetitionLoopCount) : ICodegenSummary;
