using System.Text.Json;
using System.Text.Json.Nodes;

using SimpleModelServer.Evals.Http;
using SimpleModelServer.Evals.Models;

namespace SimpleModelServer.Evals.Router;

/// <summary>
/// Mode: router (A5). Sends every golden case's prompt to the router model via the OpenAI-compatible
/// chat completions endpoint, JSON-schema-constrained to the shape in
/// <c>tests/prompts/router-schema.json</c> (same schema/system-prompt convention as the A4 benchmark
/// in <c>build/scripts/phase0-benchmarks.ps1</c>), and compares the parsed fields against
/// <c>expected</c>.
/// </summary>
public sealed class RouterRunner(LlamaChatClient client) : IRouterRunner
{
	public async Task<(IReadOnlyList<RouterCaseResult> Cases, RouterSummary Summary)> RunAsync(
		string routerUrl,
		IReadOnlyList<GoldenCase> cases,
		JsonNode schema,
		string systemPrompt,
		int maxTokens,
		int timeoutSeconds,
		CancellationToken cancellationToken = default)
	{
		List<RouterCaseResult> results = [];

		foreach (GoldenCase c in cases)
		{
			using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

			List<ChatMessage> messages =
			[
				new ChatMessage("system", systemPrompt),
				new ChatMessage("user", c.Prompt)
			];

			IChatCompletionResult response = await client.CompleteAsync(
				routerUrl, messages, temperature: 0.0, maxTokens: maxTokens, jsonSchema: schema,
				cancellationToken: cts.Token);

			results.Add(ParseCaseResult(c, response));
		}

		return (results, Summarize(results));
	}

	async Task<(IReadOnlyList<IRouterCaseResult> Cases, IRouterSummary Summary)> IRouterRunner.RunAsync(
		string routerUrl,
		IReadOnlyList<IGoldenCase> cases,
		JsonNode schema,
		string systemPrompt,
		int maxTokens,
		int timeoutSeconds,
		CancellationToken cancellationToken)
	{
		var concreteCases = cases.Cast<GoldenCase>().ToList();
		return await RunAsync(routerUrl, concreteCases, schema, systemPrompt, maxTokens, timeoutSeconds, cancellationToken);
	}

	private static RouterCaseResult ParseCaseResult(GoldenCase c, IChatCompletionResult response)
	{
		if (!response.Succeeded)
		{
			return new RouterCaseResult(
				c.Id, c.Expected.Intent, null, c.Expected.Quality, null, c.Expected.Ambiguity, null,
				c.Expected.RoutingReason, null, false, response.LatencyMs, response.Error);
		}

		try
		{
			RouterResponse? parsed = JsonSerializer.Deserialize<RouterResponse>(response.Content);
			bool valid = parsed is { Intent: not null, Quality: not null, Ambiguity: not null, RoutingReason: not null };

			return new RouterCaseResult(
				c.Id, c.Expected.Intent, parsed?.Intent, c.Expected.Quality, parsed?.Quality,
				c.Expected.Ambiguity, parsed?.Ambiguity, c.Expected.RoutingReason, parsed?.RoutingReason,
				valid, response.LatencyMs, valid ? null : "schema-invalid response");
		}
		catch (JsonException ex)
		{
			return new RouterCaseResult(
				c.Id, c.Expected.Intent, null, c.Expected.Quality, null, c.Expected.Ambiguity, null,
				c.Expected.RoutingReason, null, false, response.LatencyMs,
				$"malformed JSON: {ex.Message}");
		}
	}

	private static RouterSummary Summarize(IReadOnlyList<RouterCaseResult> results)
	{
		int total = results.Count;
		int schemaValid = results.Count(r => r.SchemaValid);
		int intentMatches = results.Count(r => r.IntentMatch);
		int routingMatches = results.Count(r => r.RoutingReasonMatch);

		Dictionary<string, int> intentConfusion = [];
		Dictionary<string, int> routingConfusion = [];
		foreach (RouterCaseResult r in results)
		{
			if (!r.IntentMatch)
			{
				string key = $"{r.ExpectedIntent}->{r.ActualIntent ?? "(none)"}";
				intentConfusion[key] = intentConfusion.GetValueOrDefault(key) + 1;
			}

			if (!r.RoutingReasonMatch)
			{
				string key = $"{r.ExpectedRoutingReason}->{r.ActualRoutingReason ?? "(none)"}";
				routingConfusion[key] = routingConfusion.GetValueOrDefault(key) + 1;
			}
		}

		double[] sortedLatencies = [.. results.Select(r => r.LatencyMs).OrderBy(x => x)];
		double p50 = Percentile(sortedLatencies, 0.50);
		double p95 = Percentile(sortedLatencies, 0.95);

		return new RouterSummary(
			total,
			schemaValid,
			total == 0 ? 0 : 100.0 * intentMatches / total,
			total == 0 ? 0 : 100.0 * routingMatches / total,
			intentConfusion,
			routingConfusion,
			p50,
			p95);
	}

	private static double Percentile(IReadOnlyList<double> sorted, double p)
	{
		if (sorted.Count == 0)
		{
			return 0;
		}

		int index = (int)Math.Floor((sorted.Count - 1) * p);
		return sorted[index];
	}
}
