using Microsoft.AspNetCore.Http;

namespace SimpleModelServer.Server.Interfaces;

/// <summary>
///     Router-specific contract beyond Ollama API compatibility. Includes health checks,
///     hardware capacity reporting, and draft-pair generation features.
/// </summary>
internal interface IRouter
{
    /// <summary>
    ///     GET /health: Full health check with self-tests and recovery attempts.
    ///     Returns 200 (ok) or 202 (warming up) or 503 (degraded/unhealthy).
    /// </summary>
    Task HandleHealthAsync(HttpContext context);

    /// <summary>
    ///     GET /health/ready: Lightweight health check for frequent polling.
    ///     Returns 200 (ok) or 503 (not ready).
    /// </summary>
    Task HandleHealthReadyAsync(HttpContext context);

    /// <summary>
    ///     GET /api/capacity: Report resolved hardware settings (CPU threads, GPU layers, max concurrent requests).
    ///     Used by external services (e.g., TokenSaver background indexer) to size their own concurrency.
    /// </summary>
    Task HandleCapacityAsync(HttpContext context);
}
