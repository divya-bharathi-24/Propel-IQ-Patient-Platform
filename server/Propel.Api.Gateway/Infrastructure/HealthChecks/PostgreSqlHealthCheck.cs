using Microsoft.Extensions.Diagnostics.HealthChecks;
using Propel.Api.Gateway.Data;
using Serilog;
using StackExchange.Redis;

namespace Propel.Api.Gateway.Infrastructure.HealthChecks;

/// <summary>
/// Health check for PostgreSQL availability (EP-011/us_052, AC-1, NFR-003).
/// Uses <see cref="AppDbContext.Database.CanConnectAsync"/> to verify connectivity.
/// On failure: sets Redis flag <c>platform:health:db_down</c> (60-second TTL) so
/// <c>ExceptionHandlingMiddleware</c> can return 503 without exposing internals (edge-case spec).
/// Logs <c>Serilog.Critical</c> on exception per NFR-003.
/// Redis flag write is best-effort — Redis unavailability does not affect the check outcome.
/// </summary>
internal sealed class PostgreSqlHealthCheck : IHealthCheck
{
    private readonly AppDbContext _dbContext;
    // IServiceProvider is used to lazily resolve IConnectionMultiplexer so that
    // Redis being disabled in development (factory throws) does not prevent the
    // PostgreSQL check from being created or running correctly.
    private readonly IServiceProvider _sp;

    public PostgreSqlHealthCheck(AppDbContext dbContext, IServiceProvider sp)
    {
        _dbContext = dbContext;
        _sp = sp;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            bool canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                await SetDbDownFlagAsync();
                return HealthCheckResult.Unhealthy("PostgreSQL unreachable");
            }

            return HealthCheckResult.Healthy("PostgreSQL reachable");
        }
        catch (Exception ex)
        {
            await SetDbDownFlagAsync();
            Log.Fatal(ex, "PostgreSQL health check failed — setting db_down flag");
            return HealthCheckResult.Unhealthy("PostgreSQL check threw exception", ex);
        }
    }

    // Best-effort: never throws. Redis may also be down (dev mode or network issue).
    private async Task SetDbDownFlagAsync()
    {
        try
        {
            IConnectionMultiplexer? multiplexer;
            try
            {
                // In development Redis is registered as a factory that throws —
                // catch and return without setting the flag.
                multiplexer = _sp.GetService<IConnectionMultiplexer>();
            }
            catch
            {
                return;
            }

            if (multiplexer is null || !multiplexer.IsConnected)
                return;

            await multiplexer.GetDatabase().StringSetAsync(
                "platform:health:db_down",
                "1",
                TimeSpan.FromSeconds(60));
        }
        catch
        {
            // Best effort — do not surface Redis failures from a PostgreSQL health check.
        }
    }
}
