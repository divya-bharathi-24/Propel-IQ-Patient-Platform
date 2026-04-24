using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using StackExchange.Redis;

namespace Propel.Api.Gateway.Infrastructure.Behaviors;

/// <summary>
/// MediatR pipeline behavior that measures handler execution time and records latency samples
/// to Redis for p95 SLA monitoring. After every 10th sample, computes the p95 percentile across
/// the last 500 samples; emits a Serilog Warning and sets a Redis debounce flag when p95 exceeds
/// the 2-second SLA (NFR-001, AC-3 edge case).
/// <para>
/// Redis operations are fire-and-forget (<c>_ = RecordLatencyAsync(...)</c>): all exceptions are
/// swallowed so latency recording never affects the primary response path (NFR-018).
/// In development mode where Redis is disabled, all recording operations are silently skipped.
/// </para>
/// </summary>
/// <typeparam name="TRequest">The MediatR request (command/query) type.</typeparam>
/// <typeparam name="TResponse">The response type returned by the handler.</typeparam>
public sealed class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private const int    SampleWindow = 500;        // Fixed-size sliding window per route key
    private const int    EvalInterval = 10;         // Evaluate p95 every Nth sample write
    private const long   SlaMs        = 2_000;      // 2-second p95 SLA threshold (NFR-001)
    private const string AlertKey     = "api:perf:p95_breach";
    private static readonly TimeSpan AlertTtl = TimeSpan.FromMinutes(10);

    // IServiceProvider is injected instead of IConnectionMultiplexer directly so that
    // the behavior can be constructed successfully in development mode where Redis
    // is disabled and IConnectionMultiplexer resolution throws (graceful degradation, NFR-018).
    private readonly IServiceProvider _serviceProvider;

    public PerformanceBehavior(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        // Fire-and-forget — never awaited so latency recording does not add to response time.
        _ = RecordLatencyAsync(typeof(TRequest).Name, sw.ElapsedMilliseconds);

        return response;
    }

    private async Task RecordLatencyAsync(string requestName, long latencyMs)
    {
        try
        {
            // Resolve Redis at call time so development mode (where factory throws) is handled
            // gracefully without breaking the DI container build or the behavior pipeline.
            var redis = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
            var db    = redis.GetDatabase();

            string listKey   = $"api:perf:latency:{requestName}";
            long sampleCount = await db.ListLeftPushAsync(listKey, latencyMs.ToString());
            await db.ListTrimAsync(listKey, 0, SampleWindow - 1);

            // Evaluate p95 only on every EvalInterval-th write to limit Redis read load.
            if (sampleCount % EvalInterval != 0) return;

            var rawSamples = await db.ListRangeAsync(listKey, 0, SampleWindow - 1);
            var samples = rawSamples
                .Where(v => v.HasValue)
                .Select(v => long.Parse((string)v!))
                .OrderBy(x => x)
                .ToList();

            // Guard: insufficient data — p95 is not statistically meaningful below 20 samples.
            if (samples.Count < 20) return;

            int p95Index = (int)Math.Ceiling(samples.Count * 0.95) - 1;
            long p95     = samples[p95Index];

            if (p95 > SlaMs)
            {
                // Debounce: only alert once per AlertTtl window to prevent log flooding (AC-3 edge case).
                bool alreadyFlagged = await db.KeyExistsAsync(AlertKey);
                if (!alreadyFlagged)
                {
                    await db.StringSetAsync(AlertKey, requestName, AlertTtl);

                    Log.Warning(
                        "API p95 latency SLA breach: {RequestName} p95={P95Ms}ms (SLA={SlaMs}ms) " +
                        "based on last {SampleCount} samples",
                        requestName, p95, SlaMs, samples.Count);
                }
            }
        }
        catch (Exception ex)
        {
            // Swallow all exceptions — latency recording must never affect the primary response path.
            Log.Error(ex, "PerformanceBehavior failed to record latency for {RequestName}", requestName);
        }
    }
}
