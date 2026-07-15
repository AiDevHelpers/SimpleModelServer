using System.Text.Json.Nodes;

using SimpleModelServer.Evals.Cli;
using SimpleModelServer.Evals.Codegen;
using SimpleModelServer.Evals.Golden;
using SimpleModelServer.Evals.Http;
using SimpleModelServer.Evals.Interfaces;
using SimpleModelServer.Evals.Models;
using SimpleModelServer.Evals.Reporting;
using SimpleModelServer.Evals.Router;

const string UsageText = """
    SimpleModelServer.Evals — golden-set eval harness for the staged pipeline.

    Usage:
      SimpleModelServer.Evals --mode router  --router-url http://localhost:PORT [--cases tests/golden] [--filter prefix]
      SimpleModelServer.Evals --mode codegen --gen-url http://localhost:PORT --label 3b-base-pair [--filter prefix] [--n-predict 1800] [--temperature 0.2]
      SimpleModelServer.Evals --mode all     --router-url http://localhost:PORT --gen-url http://localhost:PORT2 --label 3b-base-pair

    Concrete runs planned for this repo:
      # A5 router classification accuracy
      SimpleModelServer.Evals --mode router --router-url http://127.0.0.1:8081 --label router-v1

      # 3B-base+0.5B-base draft pair, codegen quality
      SimpleModelServer.Evals --mode codegen --gen-url http://127.0.0.1:8080 --label 3b-base-pair --n-predict 1800 --temperature 0.2

      # 7B-instruct+0.5B draft pair, codegen quality (head-to-head vs the 3B run above)
      SimpleModelServer.Evals --mode codegen --gen-url http://127.0.0.1:8080 --label 7b-instruct-pair --n-predict 1800 --temperature 0.2

    The harness never starts or stops llama-server itself — point --router-url/--gen-url at
    already-running instances. See src/SimpleModelServer.Evals/README.md for launch commands.
    """;

EvalOptions options;
try
{
    options = EvalOptions.Parse(args);
}
catch (HelpRequestedException)
{
    Console.WriteLine(UsageText);
    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(UsageText);
    return 1;
}

string repoRoot = ResolveRepoRoot(options.CasesDir);
string gitSha = GitInfo.GetCurrentSha(repoRoot);

IReadOnlyList<GoldenCase> allCases = GoldenCaseLoader.Load(options.CasesDir, options.Filter);
if (allCases.Count == 0)
{
    Console.Error.WriteLine($"No golden cases matched (dir: {options.CasesDir}, filter: {options.Filter ?? "(none)"}).");
    return 1;
}

using LlamaChatClient client = new(TimeSpan.FromSeconds(options.TimeoutSeconds + 30));
int exitCode = 0;

if (options.Mode is EvalMode.Router or EvalMode.All)
{
    exitCode |= await RunRouterAsync(options, allCases, client, repoRoot, gitSha);
}

if (options.Mode is EvalMode.Codegen or EvalMode.All)
{
    exitCode |= await RunCodegenAsync(options, allCases, client, gitSha);
}

return exitCode;

static string ResolveRepoRoot(string casesDir)
{
    // tests/golden -> repo root is two levels up; fall back to cwd if the layout doesn't match
    // (e.g. --cases pointed somewhere else).
    string full = Path.GetFullPath(casesDir);
    DirectoryInfo? parent = Directory.GetParent(full)?.Parent;
    return parent is not null && Directory.Exists(Path.Combine(parent.FullName, ".git"))
        ? parent.FullName
        : Directory.GetCurrentDirectory();
}

static async Task<int> RunRouterAsync(
    EvalOptions options, IReadOnlyList<GoldenCase> allCases, LlamaChatClient client, string repoRoot, string gitSha)
{
    string schemaPath = Path.Combine(repoRoot, "tests", "prompts", "router-schema.json");
    if (!File.Exists(schemaPath))
    {
        Console.Error.WriteLine($"Router schema not found at {schemaPath}");
        return 1;
    }

    JsonNode schema = JsonNode.Parse(await File.ReadAllTextAsync(schemaPath))
        ?? throw new InvalidDataException($"Failed to parse {schemaPath}");

    string systemPromptPath = Path.Combine(repoRoot, "tests", "prompts", "router-system-prompt.txt");
    if (!File.Exists(systemPromptPath))
    {
        Console.Error.WriteLine($"Router system prompt not found at {systemPromptPath}");
        return 1;
    }

    string systemPrompt = (await File.ReadAllTextAsync(systemPromptPath)).Trim();

    Console.WriteLine($"== Router mode: {allCases.Count} cases against {options.RouterUrl} ==");

    RouterRunner runner = new(client);
    (IReadOnlyList<RouterCaseResult> cases, RouterSummary summary) = await runner.RunAsync(
        options.RouterUrl!, allCases, schema, systemPrompt, options.NPredict, options.TimeoutSeconds);

    string? modelInfo = await client.TryGetModelInfoAsync(options.RouterUrl!);

    ReportMetadata meta = new("router", options.Label, gitSha, DateTimeOffset.UtcNow, options.RouterUrl, null, modelInfo);
    string json = RouterReportBuilder.BuildJson(meta, cases, summary);
    string md = RouterReportBuilder.BuildMarkdown(meta, cases, summary);
    ReportPaths paths = ReportWriter.Write(options.ReportsDir, meta, json, md);

    Console.WriteLine();
    Console.WriteLine($"Routing reason accuracy: {summary.RoutingReasonAccuracyPct:F1}%  |  Intent accuracy: {summary.IntentAccuracyPct:F1}%");
    Console.WriteLine($"Schema valid: {summary.SchemaValidCount}/{summary.TotalCases}  |  Latency p50/p95: {summary.P50LatencyMs:F0}/{summary.P95LatencyMs:F0} ms");
    Console.WriteLine($"Reports written: {paths.JsonPath}, {paths.MdPath}");

    return 0;
}

static async Task<int> RunCodegenAsync(EvalOptions options, IReadOnlyList<GoldenCase> allCases, LlamaChatClient client, string gitSha)
{
    IReadOnlyList<GoldenCase> codegenCases = CodegenRunner.SelectCases(allCases);
    if (codegenCases.Count == 0)
    {
        Console.Error.WriteLine("No cases with expected.routing_reason == \"codegen\" matched the filter.");
        return 1;
    }

    Console.WriteLine($"== Codegen mode: {codegenCases.Count} cases against {options.GenUrl} (label: {options.Label}) ==");

    IGenerationPipeline pipeline = new DirectGenerationPipeline(client, options.GenUrl!, options.Temperature, options.NPredict);
    CodegenRunner runner = new(pipeline);
    (IReadOnlyList<CodegenCaseResult> cases, CodegenSummary summary) = await runner.RunAsync(codegenCases, options.TimeoutSeconds);

    string? modelInfo = await client.TryGetModelInfoAsync(options.GenUrl!);

    ReportMetadata meta = new("codegen", options.Label, gitSha, DateTimeOffset.UtcNow, null, options.GenUrl, modelInfo);
    string json = CodegenReportBuilder.BuildJson(meta, cases, summary);
    string md = CodegenReportBuilder.BuildMarkdown(meta, cases, summary);
    ReportPaths paths = ReportWriter.Write(options.ReportsDir, meta, json, md);

    Console.WriteLine();
    Console.WriteLine($"Compile-pass rate: {summary.CompilePassRatePct:F1}%  |  Fully-clean rate: {summary.FullyCleanRatePct:F1}%");
    Console.WriteLine($"Parse-pass: {summary.ParsePassRatePct:F1}%  |  Assertion-pass: {summary.AssertionPassRatePct:F1}%  |  Repetition loops: {summary.RepetitionLoopCount}");
    Console.WriteLine($"Reports written: {paths.JsonPath}, {paths.MdPath}");

    return 0;
}
