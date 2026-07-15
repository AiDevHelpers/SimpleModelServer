namespace SimpleModelServer.Evals.Codegen;

public interface IGateDiagnostic
{
    string Id { get; }
    string Message { get; }
    string Severity { get; }
    int Line { get; }
}
