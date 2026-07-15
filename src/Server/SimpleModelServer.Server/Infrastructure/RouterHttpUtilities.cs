using SimpleModelServer.Config.Configuration;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using System.Text;
using System.Text.Json;

namespace SimpleModelServer.Server;

/// <summary>
///     Small HTTP request/response helpers shared between <see cref="RouterHttpServer" /> and
///     <see cref="AdminEndpointHandler" /> so the two don't each carry their own copy of body
///     reading/writing plumbing.
/// </summary>
internal static partial class RouterHttpUtilities
{
    [LoggerMessage(Level = LogLevel.Information, Message = "[{Method}] {Path} body ({Length} chars):\n{Body}")]
    private static partial void LogRequestBody(ILogger logger, string method, string path, int length, string body);

    internal static string RemoteAddress(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    ///     Reads a request body, optionally logging a (truncated) preview when
    ///     global.debug.logRequestBodies is enabled. Verbose and potentially sensitive (bodies
    ///     carry prompt text) — off by default; intended for short local debugging sessions only.
    /// </summary>
    internal static async Task<string> ReadBodyAsync(HttpRequest request, IAppConfig config, ILogger logger)
    {
        string body;
        using (StreamReader reader = new(request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync();
        }

        if (config.Global.Debug.LogRequestBodies)
        {
            const int MaxBodyLog = 2000;
            string preview = body.Length > MaxBodyLog
                ? $"{body[..MaxBodyLog]}… (+{body.Length - MaxBodyLog} more chars)"
                : body;
            LogRequestBody(logger, request.Method.ToUpperInvariant(), request.Path.Value ?? "/", body.Length, preview);
        }

        return body;
    }

    internal static async Task WriteJsonAsync(HttpContext context, int statusCode, object body)
    {
        string json = JsonSerializer.Serialize(body);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes);
    }
}
