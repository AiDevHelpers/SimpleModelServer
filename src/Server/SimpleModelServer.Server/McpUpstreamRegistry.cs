using SimpleModelServer.Contracts.Configuration;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;
using System.Linq;

namespace SimpleModelServer.Server;

/// <summary>Point-in-time status of one configured MCP upstream — see <see cref="McpUpstreamRegistry.GetStatus" />.</summary>
internal readonly record struct McpUpstreamStatus(string Url, bool Connected, int ToolCount);

/// <summary>
///     Aggregates tools from multiple upstream MCP servers behind one gateway endpoint
///     (mapped at /mcp by <see cref="RouterHttpServer.StartAsync" />). HTTP upstreams only —
///     no stdio process management, since no real stdio MCP server exists to target (see
///     <see cref="IMcpUpstreamConfig" />). One long-lived <see cref="McpClient" /> per
///     configured upstream, shared across all downstream gateway callers — most MCP servers are
///     stateless request/response, so per-caller isolation isn't built until a concrete upstream
///     actually needs it. <see cref="InitializeAsync" /> is called once, after the Kestrel host
///     is built; an upstream that's unreachable at that point is logged and skipped rather than
///     failing the whole router's startup, matching this codebase's existing graceful-degradation
///     style (e.g. LM Studio's no-process-control path in <see cref="ModelHealthMonitor" />).
/// </summary>
internal sealed partial class McpUpstreamRegistry(
    IReadOnlyDictionary<string, IMcpUpstreamConfig> upstreams,
    ILogger logger)
{
    private readonly ConcurrentDictionary<string, McpClient> clientsByServer =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (string ServerName, string OriginalToolName)> toolRoutes =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Tool> aggregatedTools =
        new(StringComparer.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP upstream '{ServerName}' connected — {ToolCount} tool(s).")]
    private static partial void LogUpstreamConnected(ILogger logger, string serverName, int toolCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP upstream '{ServerName}' ({Url}) unreachable at startup — skipped.")]
    private static partial void LogUpstreamUnreachable(ILogger logger, Exception ex, string serverName, string url);

    /// <summary>Connects to every configured upstream and caches its tool list, namespaced as "serverName.toolName".</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        foreach ((string serverName, IMcpUpstreamConfig upstreamConfig) in upstreams)
        {
            try
            {
                McpClient client = await McpClient.CreateAsync(
                    new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(upstreamConfig.Url) }),
                    cancellationToken: ct);

                IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: ct);
                foreach (McpClientTool tool in tools)
                {
                    string fullName = $"{serverName}.{tool.Name}";
                    toolRoutes[fullName] = (serverName, tool.Name);
                    aggregatedTools[fullName] = new Tool
                    {
                        Name = fullName,
                        Description = tool.Description,
                        InputSchema = tool.JsonSchema
                    };
                }

                clientsByServer[serverName] = client;
                LogUpstreamConnected(logger, serverName, tools.Count);
            }
            catch (Exception ex)
            {
                LogUpstreamUnreachable(logger, ex, serverName, upstreamConfig.Url);
            }
        }
    }

    /// <summary>
    ///     Per-upstream status for display purposes (system info API, admin UI) — configured URL,
    ///     whether <see cref="InitializeAsync" /> successfully connected to it, and how many of
    ///     its tools got aggregated. Reflects a point-in-time snapshot from the initial connect
    ///     attempt; an upstream that goes down afterward still reports as connected here (no
    ///     periodic health re-check for MCP upstreams, unlike Ollama/LM Studio's own health
    ///     monitoring).
    /// </summary>
    public IReadOnlyDictionary<string, McpUpstreamStatus> GetStatus()
    {
        return upstreams.ToDictionary(
            kvp => kvp.Key,
            kvp => new McpUpstreamStatus(
                kvp.Value.Url,
                clientsByServer.ContainsKey(kvp.Key),
                aggregatedTools.Keys.Count(name => name.StartsWith(kvp.Key + ".", StringComparison.OrdinalIgnoreCase))),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Matches <see cref="Microsoft.Extensions.DependencyInjection.McpServerBuilderExtensions.WithListToolsHandler" />'s delegate shape.</summary>
    public ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> context, CancellationToken ct)
        => ValueTask.FromResult(new ListToolsResult { Tools = [.. aggregatedTools.Values] });

    /// <summary>Matches <see cref="Microsoft.Extensions.DependencyInjection.McpServerBuilderExtensions.WithCallToolHandler" />'s delegate shape — strips the "serverName." prefix and forwards to that upstream.</summary>
    public async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> context, CancellationToken ct)
    {
        string fullName = context.Params?.Name ?? "";
        if (!toolRoutes.TryGetValue(fullName, out (string ServerName, string OriginalToolName) route) ||
            !clientsByServer.TryGetValue(route.ServerName, out McpClient? client))
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Unknown tool '{fullName}'." }]
            };
        }

        IReadOnlyDictionary<string, object?>? arguments = context.Params?.Arguments?
            .ToDictionary(kv => kv.Key, object? (kv) => kv.Value);
        return await client.CallToolAsync(route.OriginalToolName, arguments, cancellationToken: ct);
    }
}
