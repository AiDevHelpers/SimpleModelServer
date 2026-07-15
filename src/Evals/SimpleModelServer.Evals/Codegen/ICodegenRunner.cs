namespace SimpleModelServer.Evals.Codegen;

using SimpleModelServer.Evals.Models;

public interface ICodegenRunner
{
    Task<(IReadOnlyList<ICodegenCaseResult> Cases, ICodegenSummary Summary)> RunAsync(
        IReadOnlyList<IGoldenCase> cases, int timeoutSeconds, CancellationToken cancellationToken = default);
}
