namespace SimpleModelServer.Evals.Reporting;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056",
	Justification = "RouterUrl/GenUrl are plain strings carried through from IEvalOptions into report metadata/JSON; changing to System.Uri would complicate serialization for no functional benefit.")]
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
