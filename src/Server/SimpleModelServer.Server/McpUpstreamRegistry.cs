using SimpleModelServer.Contracts.Configuration;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;
using System.Linq;

namespace SimpleModelServer.Server;

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
///     Implements the public <see cref="IMcpUpstreamRegistry" /> so
///     <c>SimpleModelServer.Mcp</c>'s self-management tools (list/add/remove MCP servers, injected
///     into this same gateway via <c>WithTools&lt;McpAdminTools&gt;()</c>) can drive it without a
///     dependency on this internal type.
/// </summary>
internal sealed partial class McpUpstreamRegistry(
    IReadOnlyDictionary<string, IMcpUpstreamConfig> upstreams,
    ILogger logger) : IMcpUpstreamRegistry
{
    private readonly ConcurrentDictionary<string, McpClient> clientsByServer =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (string ServerName, string OriginalToolName)> toolRoutes =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Tool> aggregatedTools =
        new(StringComparer.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP upstream '{ServerName}' connected — {ToolCount} tool(s).")]
    private static partial void LogUpstreamConnected(ILogger logger, string serverName, int toolCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP upstream '{ServerName}' ({Path}) unreachable — skipped.")]
    private static partial void LogUpstreamUnreachable(ILogger logger, Exception ex, string serverName, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP upstream '{ServerName}' configured with transport {TransportType} — not yet supported, skipped.")]
    private static partial void LogUpstreamTransportUnsupported(ILogger logger, string serverName, McpTransportType transportType);

    /// <summary>Connects to every configured upstream and caches its tool list, namespaced as "serverName.toolName".</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        foreach ((string serverName, IMcpUpstreamConfig upstreamConfig) in upstreams)
        {
            await ConnectAsync(serverName, upstreamConfig, ct);
        }
    }

    /// <summary>
    ///     Connects to a single upstream and aggregates its tools — the same logic
    ///     <see cref="InitializeAsync" /> runs for every configured upstream at startup, exposed
    ///     separately so the admin "add MCP server" endpoint can bring a newly configured upstream
    ///     online immediately instead of requiring a restart. An unreachable upstream, or one
    ///     configured with a transport this registry doesn't yet support (see
    ///     <see cref="McpTransportType" />), is logged and left disconnected rather than throwing —
    ///     matching <see cref="InitializeAsync" />'s graceful-degradation behavior.
    /// </summary>
    public async Task ConnectAsync(string serverName, IMcpUpstreamConfig upstreamConfig, CancellationToken ct = default)
    {
        if (upstreamConfig.TransportType != McpTransportType.Http)
        {
            LogUpstreamTransportUnsupported(logger, serverName, upstreamConfig.TransportType);
            return;
        }

        try
        {
            McpClient client = await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(upstreamConfig.Path) }),
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
            LogUpstreamUnreachable(logger, ex, serverName, upstreamConfig.Path);
        }
    }

    /// <summary>
    ///     Disconnects and removes an upstream's client and aggregated tools — the counterpart to
    ///     <see cref="ConnectAsync" />, used by the admin "remove MCP server" endpoint so a removed
    ///     upstream's tools stop being routable immediately. Safe to call for an upstream that was
    ///     never connected (e.g. it was unreachable at startup or is a stdio upstream) — it's just a
    ///     no-op in that case.
    /// </summary>
    public async Task DisconnectAsync(string serverName)
    {
        foreach (string fullName in toolRoutes
                     .Where(kv => kv.Value.ServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase))
                     .Select(kv => kv.Key)
                     .ToList())
        {
            toolRoutes.TryRemove(fullName, out _);
            aggregatedTools.TryRemove(fullName, out _);
        }

        if (clientsByServer.TryRemove(serverName, out McpClient? client))
        {
            await client.DisposeAsync();
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
                kvp.Value.TransportType,
                kvp.Value.Path,
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
