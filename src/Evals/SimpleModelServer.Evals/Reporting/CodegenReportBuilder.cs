using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using SimpleModelServer.Evals.Codegen;

namespace SimpleModelServer.Evals.Reporting;

public static class CodegenReportBuilder
{
    public static string BuildJson(ReportMetadata meta, IReadOnlyList<CodegenCaseResult> cases, CodegenSummary summary)
    {
        JsonObject root = new()
        {
            ["mode"] = meta.Mode,
            ["label"] = meta.Label,
            ["git_sha"] = meta.GitSha,
            ["timestamp_utc"] = meta.TimestampUtc.ToString("O"),
            ["gen_url"] = meta.GenUrl,
            ["model_info"] = meta.ModelInfo,
            ["headline"] = new JsonObject
            {
                ["compile_pass_rate_pct"] = Math.Round(summary.CompilePassRatePct, 2),
                ["fully_clean_rate_pct"] = Math.Round(summary.FullyCleanRatePct, 2)
            },
            ["summary"] = new JsonObject
            {
                ["total_cases"] = summary.TotalCases,
                ["parse_pass_rate_pct"] = Math.Round(summary.ParsePassRatePct, 2),
                ["compile_pass_rate_pct"] = Math.Round(summary.CompilePassRatePct, 2),
                ["assertion_pass_rate_pct"] = Math.Round(summary.AssertionPassRatePct, 2),
                ["fully_clean_rate_pct"] = Math.Round(summary.FullyCleanRatePct, 2),
                ["repetition_loop_count"] = summary.RepetitionLoopCount
            },
            ["cases"] = new JsonArray([.. cases.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.CaseId,
                ["generation_succeeded"] = c.GenerationSucceeded,
                ["generation_error"] = c.GenerationError,
                ["parse_passed"] = c.ParsePassed,
                ["compile_passed"] = c.CompilePassed,
                ["assertions_passed"] = c.AssertionsPassed,
                ["fully_clean"] = c.FullyClean,
                ["repetition_loop_detected"] = c.RepetitionLoopDetected,
                ["finish_reason"] = c.FinishReason,
                ["completion_tokens"] = c.CompletionTokens,
                ["latency_ms"] = Math.Round(c.LatencyMs, 1),
                ["parse_diagnostics"] = JsonSerializer.SerializeToNode(c.ParseDiagnostics),
                ["compile_diagnostics"] = JsonSerializer.SerializeToNode(c.CompileDiagnostics),
                ["assertions"] = JsonSerializer.SerializeToNode(c.Assertions)
            })])
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string BuildMarkdown(ReportMetadata meta, IReadOnlyList<CodegenCaseResult> cases, CodegenSummary summary)
    {
        StringBuilder sb = new();
        sb.AppendLine($"# Codegen eval — {meta.Label}");
        sb.AppendLine();
        sb.AppendLine($"- Git SHA: `{meta.GitSha}`");
        sb.AppendLine($"- Timestamp (UTC): {meta.TimestampUtc:O}");
        sb.AppendLine($"- Generation URL: {meta.GenUrl}");
        if (meta.ModelInfo is not null)
        {
            sb.AppendLine($"- Model: {meta.ModelInfo}");
        }

        sb.AppendLine();
        sb.AppendLine("## Headline");
        sb.AppendLine();
        sb.AppendLine($"- **Compile-pass rate: {summary.CompilePassRatePct:F1}%**");
        sb.AppendLine($"- Fully-clean rate (parse + compile + assertions): {summary.FullyCleanRatePct:F1}%");
        sb.AppendLine($"- Parse-pass rate: {summary.ParsePassRatePct:F1}%");
        sb.AppendLine($"- Assertion-pass rate: {summary.AssertionPassRatePct:F1}%");
        sb.AppendLine($"- Repetition-loop cases: {summary.RepetitionLoopCount}/{summary.TotalCases}");
        sb.AppendLine();

        sb.AppendLine("## Per-case results");
        sb.AppendLine();
        sb.AppendLine("| Case | Parse | Compile | Assertions | Fully clean | Finish reason | Latency (ms) |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (CodegenCaseResult c in cases)
        {
            sb.AppendLine($"| {c.CaseId} | {Mark(c.ParsePassed)} | {Mark(c.CompilePassed)} | " +
                          $"{Mark(c.AssertionsPassed)} | {Mark(c.FullyClean)} | {c.FinishReason ?? "(n/a)"} | {c.LatencyMs:F0} |");
        }

        List<CodegenCaseResult> failures = [.. cases.Where(c => !c.FullyClean)];
        if (failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Failure detail");
            sb.AppendLine();
            foreach (CodegenCaseResult c in failures)
            {
                sb.AppendLine($"### {c.CaseId}");
                sb.AppendLine();
                if (!c.GenerationSucceeded)
                {
                    sb.AppendLine($"- Generation error: {c.GenerationError}");
                }

                foreach (GateDiagnostic d in c.ParseDiagnostics)
                {
                    sb.AppendLine($"- Parse: `{d.Id}` line {d.Line}: {d.Message}");
                }

                foreach (GateDiagnostic d in c.CompileDiagnostics)
                {
                    sb.AppendLine($"- Compile: `{d.Id}` line {d.Line}: {d.Message}");
                }

                foreach (AssertionOutcome a in c.Assertions.Where(a => !a.Passed))
                {
                    sb.AppendLine($"- Assertion failed: {(a.Required ? "must_contain" : "must_not_contain")} `{a.Text}`");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string Mark(bool value) => value ? "PASS" : "FAIL";
}
