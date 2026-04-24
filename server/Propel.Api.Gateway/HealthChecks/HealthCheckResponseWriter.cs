using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Propel.Api.Gateway.HealthChecks;

/// <summary>
/// Custom ASP.NET Core health check response writer (EP-011/us_052, AC-1, NFR-003).
/// Serialises <see cref="HealthReport"/> to a structured JSON payload with no internal
/// details exposed — connection strings, hostnames, API keys, and exception messages are
/// intentionally excluded to prevent information disclosure (OWASP A09).
///
/// Response shape:
/// <code>
/// {
///   "status": "Healthy|Degraded|Unhealthy",
///   "totalDurationMs": 42,
///   "checks": [
///     { "name": "postgresql", "status": "Healthy", "description": "PostgreSQL reachable", "durationMs": 8 },
///     ...
///   ]
/// }
/// </code>
/// </summary>
public static class HealthCheckResponseWriter
{
    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var result = new
        {
            status          = report.Status.ToString(),
            totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks          = report.Entries.Select(e => new
            {
                name        = e.Key,
                status      = e.Value.Status.ToString(),
                description = e.Value.Description ?? string.Empty,
                durationMs  = (int)e.Value.Duration.TotalMilliseconds,
                // ExceptionMessage and Data are deliberately omitted to prevent
                // connection strings, hostnames, or API keys from leaking (OWASP A09).
            })
        };

        return context.Response.WriteAsJsonAsync(result,
            cancellationToken: context.RequestAborted);
    }
}
