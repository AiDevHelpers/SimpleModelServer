namespace SimpleModelServer.Evals.Codegen;

public interface IAssertionOutcome
{
    string Text { get; }
    bool Required { get; }
    bool Present { get; }
    bool Passed { get; }
}
