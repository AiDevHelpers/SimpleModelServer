using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using SimpleModelServer.Evals.Router;

namespace SimpleModelServer.Evals.Reporting;

public static class RouterReportBuilder
{
    public static string BuildJson(ReportMetadata meta, IReadOnlyList<RouterCaseResult> cases, RouterSummary summary)
    {
        JsonObject root = new()
        {
            ["mode"] = meta.Mode,
            ["label"] = meta.Label,
            ["git_sha"] = meta.GitSha,
            ["timestamp_utc"] = meta.TimestampUtc.ToString("O"),
            ["router_url"] = meta.RouterUrl,
            ["model_info"] = meta.ModelInfo,
            ["headline"] = new JsonObject
            {
                ["routing_reason_accuracy_pct"] = Math.Round(summary.RoutingReasonAccuracyPct, 2),
                ["intent_accuracy_pct"] = Math.Round(summary.IntentAccuracyPct, 2)
            },
            ["summary"] = new JsonObject
            {
                ["total_cases"] = summary.TotalCases,
                ["schema_valid_count"] = summary.SchemaValidCount,
                ["p50_latency_ms"] = Math.Round(summary.P50LatencyMs, 1),
                ["p95_latency_ms"] = Math.Round(summary.P95LatencyMs, 1),
                ["intent_confusion"] = JsonSerializer.SerializeToNode(summary.IntentConfusion),
                ["routing_reason_confusion"] = JsonSerializer.SerializeToNode(summary.RoutingReasonConfusion)
            },
            ["cases"] = new JsonArray([.. cases.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.CaseId,
                ["expected_intent"] = c.ExpectedIntent,
                ["actual_intent"] = c.ActualIntent,
                ["expected_quality"] = c.ExpectedQuality,
                ["actual_quality"] = c.ActualQuality,
                ["expected_ambiguity"] = c.ExpectedAmbiguity,
                ["actual_ambiguity"] = c.ActualAmbiguity,
                ["expected_routing_reason"] = c.ExpectedRoutingReason,
                ["actual_routing_reason"] = c.ActualRoutingReason,
                ["schema_valid"] = c.SchemaValid,
                ["routing_reason_match"] = c.RoutingReasonMatch,
                ["latency_ms"] = Math.Round(c.LatencyMs, 1),
                ["error"] = c.Error
            })])
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string BuildMarkdown(ReportMetadata meta, IReadOnlyList<RouterCaseResult> cases, RouterSummary summary)
    {
        StringBuilder sb = new();
        sb.AppendLine($"# Router eval — {meta.Label}");
        sb.AppendLine();
        sb.AppendLine($"- Git SHA: `{meta.GitSha}`");
        sb.AppendLine($"- Timestamp (UTC): {meta.TimestampUtc:O}");
        sb.AppendLine($"- Router URL: {meta.RouterUrl}");
        if (meta.ModelInfo is not null)
        {
            sb.AppendLine($"- Model: {meta.ModelInfo}");
        }

        sb.AppendLine();
        sb.AppendLine("## Headline");
        sb.AppendLine();
        sb.AppendLine($"- **Routing reason accuracy: {summary.RoutingReasonAccuracyPct:F1}%**");
        sb.AppendLine($"- Intent accuracy: {summary.IntentAccuracyPct:F1}%");
        sb.AppendLine($"- Schema-valid responses: {summary.SchemaValidCount}/{summary.TotalCases}");
        sb.AppendLine($"- Latency p50/p95: {summary.P50LatencyMs:F0} ms / {summary.P95LatencyMs:F0} ms");
        sb.AppendLine();

        if (summary.RoutingReasonConfusion.Count > 0)
        {
            sb.AppendLine("## Routing-reason confusion (expected -> actual)");
            sb.AppendLine();
            foreach ((string key, int count) in summary.RoutingReasonConfusion.OrderByDescending(kv => kv.Value))
            {
                sb.AppendLine($"- {key}: {count}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Per-case results");
        sb.AppendLine();
        sb.AppendLine("| Case | Expected reason | Actual reason | Match | Schema valid | Latency (ms) |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (RouterCaseResult c in cases)
        {
            sb.AppendLine($"| {c.CaseId} | {c.ExpectedRoutingReason} | {c.ActualRoutingReason ?? "(none)"} | " +
                          $"{(c.RoutingReasonMatch ? "PASS" : "FAIL")} | {c.SchemaValid} | {c.LatencyMs:F0} |");
        }

        return sb.ToString();
    }
}
