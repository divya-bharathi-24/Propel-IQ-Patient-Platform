using Microsoft.SemanticKernel;
using Propel.Modules.AI.Exceptions;
using Serilog;
using StackExchange.Redis;

namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Semantic Kernel <see cref="IFunctionInvocationFilter"/> that implements a Redis-backed
/// circuit breaker for AI provider calls (AIR-O02, US_050 AC-1).
/// <para>
/// The circuit transitions:
/// <list type="bullet">
///   <item><description><b>Closed</b> — normal operation; failures are counted in Redis.</description></item>
///   <item><description><b>Open</b> — after 3 consecutive failures within a 5-minute window;
///     all subsequent requests throw <see cref="CircuitBreakerOpenException"/> immediately
///     without calling the provider.</description></item>
///   <item><description><b>Reset</b> — automatic when the Redis TTL expires (open duration).
///     A subsequent failure re-trips the breaker with exponential backoff
///     (<c>5min × 2^(tripCount−1)</c>).</description></item>
/// </list>
/// </para>
/// <para>
/// State keys:
/// <list type="bullet">
///   <item><description><c>ai:cb:open</c> — present (TTL) when circuit is open.</description></item>
///   <item><description><c>ai:cb:failures</c> — consecutive failure counter (TTL = window duration).</description></item>
///   <item><description><c>ai:cb:trips:{yyyyMMddHH}</c> — trip count within the current hour for
///     exponential backoff calculation (TTL = 2 hours).</description></item>
/// </list>
/// </para>
/// <para>
/// Graceful degradation: if Redis is unavailable, the circuit is treated as closed so
/// the AI pipeline continues operating. Redis errors are swallowed and logged at Warning level
/// so they never escalate to callers (NFR-018).
/// </para>
/// </summary>
public sealed class CircuitBreakerFilter : IFunctionInvocationFilter
{
    private const string OpenKey         = "ai:cb:open";
    private const string CountKey        = "ai:cb:failures";
    private const string TripHourKeyFmt  = "ai:cb:trips:{0}";
    private const int    FailureThreshold = 3;

    private static readonly TimeSpan WindowTtl    = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TripHourTtl  = TimeSpan.FromHours(2);

    private readonly IConnectionMultiplexer? _redis;

    public CircuitBreakerFilter(IConnectionMultiplexer? redis)
    {
        _redis = redis;
    }

    /// <inheritdoc/>
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        // Abort immediately if the circuit is open.
        if (await IsCircuitOpenAsync())
        {
            throw new CircuitBreakerOpenException(
                "AI circuit breaker is open — manual review required (AIR-O02).");
        }

        try
        {
            await next(context);

            // Success: clear the consecutive failure counter.
            await ResetFailureCountAsync();
        }
        catch (Exception ex) when (ex is not CircuitBreakerOpenException
                                       and not OperationCanceledException)
        {
            // Provider call failed — record it and potentially trip the circuit.
            await HandleProviderFailureAsync();
            throw;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<bool> IsCircuitOpenAsync()
    {
        if (_redis is null)
            return false; // No Redis — treat as closed (graceful degradation).

        try
        {
            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync(OpenKey);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "CircuitBreakerFilter_RedisError: failed to check open key — treating circuit as closed (NFR-018).");
            return false;
        }
    }

    private async Task ResetFailureCountAsync()
    {
        if (_redis is null)
            return;

        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(CountKey);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "CircuitBreakerFilter_RedisError: failed to reset failure counter — continuing (NFR-018).");
        }
    }

    private async Task HandleProviderFailureAsync()
    {
        if (_redis is null)
            return;

        try
        {
            var db = _redis.GetDatabase();

            long count = await db.StringIncrementAsync(CountKey);

            // Start the 5-minute failure window on the first failure in a sequence.
            if (count == 1)
                await db.KeyExpireAsync(CountKey, WindowTtl);

            if (count < FailureThreshold)
                return;

            // Threshold reached — trip the circuit.
            string hourKey = string.Format(TripHourKeyFmt, DateTime.UtcNow.ToString("yyyyMMddHH"));
            long trips = await db.StringIncrementAsync(hourKey);
            await db.KeyExpireAsync(hourKey, TripHourTtl);

            // Exponential backoff: 5 min × 2^(trips-1), capped at 60 minutes.
            double openMinutes  = Math.Min(5 * Math.Pow(2, trips - 1), 60);
            var    openTtl      = TimeSpan.FromMinutes(openMinutes);

            // NX: only set if not already open (prevents multiple concurrent trips from extending TTL).
            bool tripped = await db.StringSetAsync(OpenKey, "1", openTtl, When.NotExists);

            if (tripped)
            {
                // Reset the failure counter so the next window starts fresh after reset.
                await db.KeyDeleteAsync(CountKey);

                Log.Error(
                    "CircuitBreakerFilter_Tripped: trip={TripCount} openMinutes={OpenMinutes} (AIR-O02, AIR-O02).",
                    trips,
                    openMinutes);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "CircuitBreakerFilter_RedisError: failed to record failure or trip circuit — continuing (NFR-018).");
        }
    }
}
