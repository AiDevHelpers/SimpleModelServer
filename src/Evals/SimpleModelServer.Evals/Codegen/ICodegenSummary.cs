namespace SimpleModelServer.Evals.Codegen;

public interface ICodegenSummary
{
    int TotalCases { get; }
    double ParsePassRatePct { get; }
    double CompilePassRatePct { get; }
    double AssertionPassRatePct { get; }
    double FullyCleanRatePct { get; }
    int RepetitionLoopCount { get; }
}
