namespace SimpleModelServer.Evals.Interfaces;

public sealed record GenerationOutcome(
	string RawResponse,
	string? FinishReason,
	int? CompletionTokens,
	double LatencyMs,
	string? Error = null) : IGenerationOutcome
{
	public bool Succeeded => Error is null;
}

/// <summary>
/// Seam between the case runner and however a prompt actually gets turned into a response. v1 is a
/// single direct call to the generation endpoint (<see cref="DirectGenerationPipeline"/>) — no
/// repair loop, because this harness measures raw model quality and the repair loop is a pipeline
/// feature tested elsewhere (see docs/staged-pipeline-test-plan.md Part C/D). A future repair-loop
/// pipeline can implement this same interface (parse/compile, on failure re-prompt with the
/// diagnostics, repeat up to N times) and be swapped in behind a `--repair` flag without touching
/// <see cref="CodegenRunner"/>.
/// </summary>
public interface IGenerationPipeline
{
	Task<GenerationOutcome> GenerateAsync(string prompt, CancellationToken cancellationToken);
}
