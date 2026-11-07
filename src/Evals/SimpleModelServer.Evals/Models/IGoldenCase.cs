namespace SimpleModelServer.Evals.Models;

public interface IGoldenCase
{
	string Id { get; }
	string Category { get; }
	string Prompt { get; }
	IExpectedResult Expected { get; }
	string? Notes { get; }
	string? StubFilePath { get; }
}
