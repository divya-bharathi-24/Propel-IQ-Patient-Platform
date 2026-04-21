using Propel.Api.Gateway.Data;
using StackExchange.Redis;

namespace Propel.Api.Gateway.Endpoints;

/// <summary>
/// Minimal API health check endpoint — satisfies AC3 and NFR-018.
///
/// GET /healthz
///   → HTTP 200 always (never 5xx; Redis unreachability is a degraded state, not an error)
///   → Body: { "status": "healthy", "db": "ok|degraded", "redis": "ok|degraded" }
///
/// Redis degradation is explicit so callers know caching is bypassed without treating it
/// as a service failure (graceful degradation per AC4 / NFR-018).
/// </summary>
public static class HealthCheckEndpoint
{
    public static void MapHealthCheck(WebApplication app)
    {
        app.MapGet("/healthz", async (AppDbContext db, IConnectionMultiplexer? redis) =>
        {
            var dbStatus = await CheckDatabaseAsync(db);
            var redisStatus = await CheckRedisAsync(redis);

            return Results.Ok(new
            {
                status = "healthy",
                db = dbStatus,
                redis = redisStatus
            });
        })
        .WithName("HealthCheck")
        .AllowAnonymous();
    }

    private static async Task<string> CheckDatabaseAsync(AppDbContext db)
    {
        try
        {
            return await db.Database.CanConnectAsync() ? "ok" : "degraded";
        }
        catch
        {
            return "degraded";
        }
    }

    private static async Task<string> CheckRedisAsync(IConnectionMultiplexer? redis)
    {
        if (redis is null || !redis.IsConnected)
        {
            return "degraded";
        }

        try
        {
            var endpoints = redis.GetEndPoints();
            if (endpoints.Length == 0)
            {
                return "degraded";
            }

            var server = redis.GetServer(endpoints[0]);
            await server.PingAsync();
            return "ok";
        }
        catch
        {
            return "degraded";
        }
    }
}
