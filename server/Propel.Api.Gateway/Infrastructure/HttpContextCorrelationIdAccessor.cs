using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure;

/// <summary>
/// Reads the correlation ID from <c>HttpContext.Items["CorrelationId"]</c> via
/// <see cref="IHttpContextAccessor"/>. The value is written there by <c>CorrelationIdMiddleware</c>
/// before any downstream handler executes (AC-2, TR-018).
/// Returns <c>"no-context"</c> when invoked outside an active HTTP request scope
/// (e.g., background jobs) to satisfy the edge-case contract defined in <see cref="ICorrelationIdAccessor"/>.
/// </summary>
public sealed class HttpContextCorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string GetCorrelationId()
        => _httpContextAccessor.HttpContext?.Items["CorrelationId"] as string
           ?? "no-context";
}
