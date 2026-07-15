namespace SimpleModelServer.Evals.Reporting;

public interface IReportMetadata
{
    string Mode { get; }
    string Label { get; }
    string GitSha { get; }
    DateTimeOffset TimestampUtc { get; }
    string? RouterUrl { get; }
    string? GenUrl { get; }
    string? ModelInfo { get; }
}
