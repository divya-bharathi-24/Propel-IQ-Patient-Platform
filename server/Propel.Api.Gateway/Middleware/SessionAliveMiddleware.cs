using System.Security.Claims;
using Propel.Modules.Auth.Services;

namespace Propel.Api.Gateway.Middleware;

/// <summary>
/// Validates that a Redis session key exists on every request targeting a route decorated with
/// <c>[Authorize]</c> (US_011, AC-2, NFR-007).
/// If the Redis key is absent (TTL expired or explicit logout), the middleware short-circuits
/// with HTTP 401 and JSON body <c>{ "error": "session_expired" }</c>.
/// On a live session, the TTL is slid back to 15 minutes (sliding-window session — NFR-007).
/// </summary>
public sealed class SessionAliveMiddleware : IMiddleware
{
    private readonly IRedisSessionService _sessionService;
    private readonly ILogger<SessionAliveMiddleware> _logger;

    public SessionAliveMiddleware(IRedisSessionService sessionService, ILogger<SessionAliveMiddleware> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Only enforce session check on authenticated requests
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        string? userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? deviceId = context.User.FindFirstValue("deviceId");

        // If either claim is missing the token is malformed — reject
        if (string.IsNullOrWhiteSpace(userIdStr) || string.IsNullOrWhiteSpace(deviceId))
        {
            await WriteSessionExpiredAsync(context);
            return;
        }

        if (!Guid.TryParse(userIdStr, out Guid userId))
        {
            await WriteSessionExpiredAsync(context);
            return;
        }

        bool alive = await _sessionService.ExistsAsync(userId, deviceId, context.RequestAborted);
        if (!alive)
        {
            _logger.LogInformation(
                "Session expired or not found for user {UserId}, device {DeviceId} — returning 401",
                userId, deviceId);

            await WriteSessionExpiredAsync(context);
            return;
        }

        // Slide the TTL on every active request (NFR-007)
        await _sessionService.ResetTtlAsync(userId, deviceId, context.RequestAborted);

        await next(context);
    }

    private static async Task WriteSessionExpiredAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "session_expired" });
    }
}
