namespace SimpleModelServer.Evals.Codegen;

public interface ICompileGateResult
{
	bool Passed { get; }
	IReadOnlyList<IGateDiagnostic> Diagnostics { get; }
}
