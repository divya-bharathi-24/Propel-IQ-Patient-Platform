using Serilog.Context;

namespace Propel.Api.Gateway.Middleware;

/// <summary>
/// Propagates a correlation ID on every request/response cycle.
/// Reads <c>X-Correlation-Id</c> from the incoming request header; if absent, generates a new GUID (compact 32-char hex).
/// Stores the value in <c>HttpContext.Items["CorrelationId"]</c>, echoes it back in the <c>X-Correlation-Id</c> response
/// header, and pushes it into Serilog <see cref="LogContext"/> so all log entries within the request scope carry
/// the <c>CorrelationId</c> enrichment property (AC-2, TR-018, AD-4).
/// </summary>
public sealed class CorrelationIdMiddleware : IMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private const string ItemsKey   = "CorrelationId";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        string correlationId = context.Request.Headers.TryGetValue(HeaderName, out var headerValue)
            && !string.IsNullOrWhiteSpace(headerValue)
            ? headerValue.ToString()
            : Guid.NewGuid().ToString("N"); // compact 32-char hex, no hyphens

        context.Items[ItemsKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
