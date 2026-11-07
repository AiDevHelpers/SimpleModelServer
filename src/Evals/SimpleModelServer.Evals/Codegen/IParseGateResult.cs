namespace SimpleModelServer.Evals.Codegen;

public interface IParseGateResult
{
	bool Passed { get; }
	IReadOnlyList<IGateDiagnostic> Diagnostics { get; }
}
