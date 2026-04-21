namespace Propel.Api.Gateway.Middleware;

/// <summary>
/// Propagates a correlation ID on every request/response cycle.
/// Reads <c>X-Correlation-ID</c> from the incoming request header; if absent, generates a new GUID.
/// Stores the value in <c>HttpContext.Items["CorrelationId"]</c> and echoes it back on the response.
/// Satisfies AC3: correlation ID injection middleware.
/// </summary>
public sealed class CorrelationIdMiddleware : IMiddleware
{
    private const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existingId)
            && !string.IsNullOrWhiteSpace(existingId)
                ? existingId.ToString()
                : Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }
}
