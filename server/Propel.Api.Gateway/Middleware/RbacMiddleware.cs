using System.Security.Claims;

namespace Propel.Api.Gateway.Middleware;

/// <summary>
/// RBAC middleware placeholder. Reads JWT claims from <c>HttpContext.User</c>
/// and logs them for observability. Actual policy enforcement is added in the
/// authentication feature story (us_003).
/// Satisfies AC3: RBAC middleware placeholder.
/// </summary>
public sealed class RbacMiddleware : IMiddleware
{
    private readonly ILogger<RbacMiddleware> _logger;

    public RbacMiddleware(ILogger<RbacMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            var role = context.User.FindFirstValue(ClaimTypes.Role) ?? "none";

            _logger.LogDebug(
                "RBAC stub — CorrelationId: {CorrelationId} | UserId: {UserId} | Role: {Role}",
                context.Items["CorrelationId"],
                userId,
                role);
        }

        await next(context);
    }
}
