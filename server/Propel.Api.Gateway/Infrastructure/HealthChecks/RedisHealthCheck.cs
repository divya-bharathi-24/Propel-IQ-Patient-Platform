using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Propel.Api.Gateway.Infrastructure.HealthChecks;

/// <summary>
/// Health check for Upstash Redis (EP-011/us_052, NFR-003, NFR-018).
/// Pings the default Redis database. The 5-second individual timeout is enforced by the
/// health check framework via the <c>timeout</c> parameter on <c>AddCheck&lt;T&gt;</c>.
/// Returns <see cref="HealthCheckResult.Degraded"/> on failure — Redis unavailability
/// degrades caching/sessions but does not make the platform fully unavailable (NFR-018).
/// </summary>
internal sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // PingAsync returns round-trip latency; no CancellationToken overload exists —
            // the framework-level timeout (registered via AddCheck timeout param) handles cancellation.
            TimeSpan latency = await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis reachable (latency {(int)latency.TotalMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Redis unreachable", ex);
        }
    }
}
