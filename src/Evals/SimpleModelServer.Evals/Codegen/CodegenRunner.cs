using SimpleModelServer.Evals.Interfaces;
using SimpleModelServer.Evals.Models;

namespace SimpleModelServer.Evals.Codegen;

/// <summary>
/// Mode: codegen (quality). Only runs cases whose <c>expected.routing_reason == "codegen"</c> — the
/// harness measures raw generation quality on the cases the router would actually forward, not the
/// full golden set.
/// </summary>
public sealed class CodegenRunner(IGenerationPipeline pipeline) : ICodegenRunner
{
	public static IReadOnlyList<GoldenCase> SelectCases(IReadOnlyList<GoldenCase> cases) =>
		[.. cases.Where(c => string.Equals(c.Expected.RoutingReason, "codegen", StringComparison.OrdinalIgnoreCase))];

	public async Task<(IReadOnlyList<CodegenCaseResult> Cases, CodegenSummary Summary)> RunAsync(
		IReadOnlyList<GoldenCase> cases, int timeoutSeconds, CancellationToken cancellationToken = default)
	{
		List<CodegenCaseResult> results = [];
		foreach (GoldenCase c in cases)
		{
			results.Add(await RunCaseAsync(c, timeoutSeconds, cancellationToken));
		}

		return (results, Summarize(results));
	}

	async Task<(IReadOnlyList<ICodegenCaseResult> Cases, ICodegenSummary Summary)> ICodegenRunner.RunAsync(
		IReadOnlyList<IGoldenCase> cases, int timeoutSeconds, CancellationToken cancellationToken)
	{
		var concreteCase = cases.Cast<GoldenCase>().ToList();
		return await RunAsync(concreteCase, timeoutSeconds, cancellationToken);
	}

	private async Task<CodegenCaseResult> RunCaseAsync(GoldenCase c, int timeoutSeconds, CancellationToken cancellationToken)
	{
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

		GenerationOutcome generation;
		try
		{
			generation = await pipeline.GenerateAsync(c.Prompt, cts.Token);
		}
		catch (OperationCanceledException)
		{
			generation = new GenerationOutcome("", null, null, timeoutSeconds * 1000.0, Error: "timeout");
		}
		catch (Exception ex)
		{
			generation = new GenerationOutcome("", null, null, 0, Error: ex.Message);
		}

		if (!generation.Succeeded)
		{
			return new CodegenCaseResult(
				c.Id, false, generation.Error, "", false, [],
				false, [], false, [], generation.LatencyMs,
				generation.CompletionTokens, generation.FinishReason, false);
		}

		string code = CodeBlockExtractor.Extract(generation.RawResponse);
		ParseGateResult parseResult = CompileGate.Parse(code);
		CompileGateResult compileResult = parseResult.Passed
			? CompileGate.Compile(code, c.StubFilePath)
			: new CompileGateResult(false, []);

		AssertionCheckResult assertionResult = AssertionChecker.Check(
			code, c.Expected.MustContainSafe, c.Expected.MustNotContainSafe);

		bool repetitionLoop = RepetitionDetector.LooksLikeRepetitionLoop(generation.RawResponse, generation.FinishReason);

		return new CodegenCaseResult(
			c.Id, true, null, code, parseResult.Passed, parseResult.Diagnostics,
			compileResult.Passed, compileResult.Diagnostics, assertionResult.AllPassed, assertionResult.Outcomes,
			generation.LatencyMs, generation.CompletionTokens, generation.FinishReason, repetitionLoop);
	}

	private static CodegenSummary Summarize(IReadOnlyList<CodegenCaseResult> results)
	{
		int total = results.Count;
		double Rate(Func<CodegenCaseResult, bool> pred) => total == 0 ? 0 : 100.0 * results.Count(pred) / total;

		return new CodegenSummary(
			total,
			Rate(r => r.ParsePassed),
			Rate(r => r.CompilePassed),
			Rate(r => r.AssertionsPassed),
			Rate(r => r.FullyClean),
			results.Count(r => r.RepetitionLoopDetected));
	}
}
