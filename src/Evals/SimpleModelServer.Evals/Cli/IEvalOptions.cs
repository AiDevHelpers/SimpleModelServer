namespace SimpleModelServer.Evals.Cli;

public interface IEvalOptions
{
    EvalMode Mode { get; }
    string? RouterUrl { get; }
    string? GenUrl { get; }
    string Label { get; }
    string CasesDir { get; }
    string? Filter { get; }
    int NPredict { get; }
    double Temperature { get; }
    int TimeoutSeconds { get; }
    string ReportsDir { get; }
}
