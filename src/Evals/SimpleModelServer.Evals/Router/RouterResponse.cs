using System.Text.Json.Serialization;

namespace SimpleModelServer.Evals.Router;

/// <summary>Mirrors the schema in <c>tests/prompts/router-schema.json</c> (the same shape the
/// staged-pipeline plan's router stage emits).</summary>
public sealed record RouterResponse(
	[property: JsonPropertyName("intent")] string? Intent,
	[property: JsonPropertyName("quality")] string? Quality,
	[property: JsonPropertyName("ambiguity")] string? Ambiguity,
	[property: JsonPropertyName("confidence")] double? Confidence,
	[property: JsonPropertyName("rewritten_prompt")] string? RewrittenPrompt,
	[property: JsonPropertyName("plan")] string? Plan,
	[property: JsonPropertyName("response")] string? Response,
	[property: JsonPropertyName("routing_reason")] string? RoutingReason) : IRouterResponse;
