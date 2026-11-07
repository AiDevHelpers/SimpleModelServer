using System.Text.Json.Serialization;

namespace SimpleModelServer.Evals.Models;

public sealed record ExpectedResult(
	[property: JsonPropertyName("intent")] string Intent,
	[property: JsonPropertyName("quality")] string Quality,
	[property: JsonPropertyName("ambiguity")] string Ambiguity,
	[property: JsonPropertyName("routing_reason")] string RoutingReason,
	[property: JsonPropertyName("must_compile")] bool MustCompile = false,
	[property: JsonPropertyName("latency_class")] string LatencyClass = "normal",
	[property: JsonPropertyName("must_contain")] IReadOnlyList<string>? MustContain = null,
	[property: JsonPropertyName("must_not_contain")] IReadOnlyList<string>? MustNotContain = null) : IExpectedResult
{
	[JsonIgnore]
	public IReadOnlyList<string> MustContainSafe => MustContain ?? [];

	[JsonIgnore]
	public IReadOnlyList<string> MustNotContainSafe => MustNotContain ?? [];
}

public sealed record GoldenCase(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("category")] string Category,
	[property: JsonPropertyName("prompt")] string Prompt,
	[property: JsonPropertyName("expected")] ExpectedResult Expected,
	[property: JsonPropertyName("notes")] string? Notes = null) : IGoldenCase
{
	/// <summary>
	/// Path to a per-case stub file (<c>tests/golden/stubs/&lt;id&gt;.cs</c>) that declares the
	/// types the prompt tells the model to "assume exist". Populated by <see cref="Golden.GoldenCaseLoader"/>
	/// when the file is present; the compile gate includes it as an extra syntax tree.
	/// </summary>
	[JsonIgnore]
	public string? StubFilePath { get; init; }

	IExpectedResult IGoldenCase.Expected => Expected;
}
