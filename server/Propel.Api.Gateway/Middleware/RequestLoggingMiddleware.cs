using System.Diagnostics;
using System.Security.Claims;
using Serilog;

namespace Propel.Api.Gateway.Middleware;

/// <summary>
/// Emits a single structured Serilog log entry per HTTP request containing all seven AC-1 fields:
/// correlation ID, route, HTTP method, status code, duration (ms), user ID, and role.
/// Must be registered after <see cref="CorrelationIdMiddleware"/> so <c>HttpContext.Items["CorrelationId"]</c>
/// is already populated before this middleware reads it (AC-1, TR-018, AD-4).
/// </summary>
public sealed class RequestLoggingMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();

            string userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
            string role   = context.User.FindFirstValue(ClaimTypes.Role) ?? "none";
            string route  = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
            string corrId = context.Items["CorrelationId"] as string ?? "unknown";

            Log.Information(
                "HTTP {Method} {Route} responded {StatusCode} in {DurationMs}ms " +
                "[CorrelationId={CorrelationId} UserId={UserId} Role={Role}]",
                context.Request.Method,
                route,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds,
                corrId,
                userId,
                role);
        }
    }
}
