using System.Text.Json;

using SimpleModelServer.Evals.Codegen;
using SimpleModelServer.Evals.Reporting;
using SimpleModelServer.Evals.Router;

using Xunit;

namespace SimpleModelServer.Evals.Tests;

public class ReportBuilderTests
{
    [Fact]
    public void RouterReportBuilder_BuildJson_IsValidJsonWithHeadlineScalars()
    {
        ReportMetadata meta = new("router", "v1", "abc123", DateTimeOffset.UtcNow, "http://localhost:8081", null, "qwen-router");
        RouterCaseResult caseResult = new(
            "trivial-enum-01", "code_generation", "code_generation", "good", "good", "none", "none",
            "codegen", "codegen", SchemaValid: true, LatencyMs: 123.4, Error: null);
        RouterSummary summary = new(1, 1, 100.0, 100.0, new Dictionary<string, int>(), new Dictionary<string, int>(), 123.4, 123.4);

        string json = RouterReportBuilder.BuildJson(meta, [caseResult], summary);
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Equal("router", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(100.0, doc.RootElement.GetProperty("headline").GetProperty("routing_reason_accuracy_pct").GetDouble());
        Assert.Equal(1, doc.RootElement.GetProperty("cases").GetArrayLength());
    }

    [Fact]
    public void CodegenReportBuilder_BuildJson_IsValidJsonWithHeadlineScalars()
    {
        ReportMetadata meta = new("codegen", "3b-base-pair", "abc123", DateTimeOffset.UtcNow, null, "http://localhost:8080", "3b-base");
        CodegenCaseResult caseResult = new(
            "trivial-enum-01", GenerationSucceeded: true, GenerationError: null, ExtractedCode: "enum X {}",
            ParsePassed: true, ParseDiagnostics: [], CompilePassed: true, CompileDiagnostics: [],
            AssertionsPassed: true, Assertions: [], LatencyMs: 500, CompletionTokens: 42, FinishReason: "stop",
            RepetitionLoopDetected: false);
        CodegenSummary summary = new(1, 100.0, 100.0, 100.0, 100.0, 0);

        string json = CodegenReportBuilder.BuildJson(meta, [caseResult], summary);
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Equal("codegen", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(100.0, doc.RootElement.GetProperty("headline").GetProperty("compile_pass_rate_pct").GetDouble());
        Assert.True(doc.RootElement.GetProperty("cases")[0].GetProperty("fully_clean").GetBoolean());
    }
}
