namespace SimpleModelServer.Evals.Reporting;

public sealed record ReportMetadata(
    string Mode,
    string Label,
    string GitSha,
    DateTimeOffset TimestampUtc,
    string? RouterUrl,
    string? GenUrl,
    string? ModelInfo) : IReportMetadata;
