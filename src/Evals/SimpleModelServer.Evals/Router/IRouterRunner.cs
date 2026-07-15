namespace SimpleModelServer.Evals.Router;

using System.Text.Json.Nodes;
using SimpleModelServer.Evals.Models;

public interface IRouterRunner
{
    Task<(IReadOnlyList<IRouterCaseResult> Cases, IRouterSummary Summary)> RunAsync(
        string routerUrl,
        IReadOnlyList<IGoldenCase> cases,
        JsonNode schema,
        string systemPrompt,
        int maxTokens,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);
}
