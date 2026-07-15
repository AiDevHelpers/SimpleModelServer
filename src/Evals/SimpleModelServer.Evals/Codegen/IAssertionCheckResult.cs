namespace SimpleModelServer.Evals.Codegen;

public interface IAssertionCheckResult
{
    IReadOnlyList<IAssertionOutcome> Outcomes { get; }
    bool AllPassed { get; }
}
