using Propel.Modules.AI.Interfaces;
using Serilog;
using StackExchange.Redis;

namespace Propel.Modules.AI.Metrics;

/// <summary>
/// Evaluates the rolling AI-Human Agreement Rate over the most recent verification events
/// and raises a Serilog warning + Redis flag when the rate drops below 98% (us_048, AC-1, AIR-Q01).
/// <para>
/// Rate computation:
/// <c>agreementRate = agreementCount / totalCount</c> over a window of 200 events.
/// </para>
/// <para>
/// Guards:
/// <list type="bullet">
///   <item><description>If <c>totalCount &lt; 50</c>: insufficient data — no alert, no Redis flag set.</description></item>
///   <item><description>If <c>agreementRate &lt; 0.98</c> and <c>totalCount ≥ 50</c>: log Serilog warning
///     and set Redis key <c>ai:quality:agreement_rate_below_threshold</c> with a 1-hour TTL.</description></item>
/// </list>
/// No automatic model rollback in Phase 1 (design.md edge case specification).
/// </para>
/// </summary>
public sealed class AgreementRateEvaluator
{
    private const int WindowSize             = 200;
    private const int MinSampleCountForAlert = 50;
    private const double AgreementThreshold  = 0.98;
    private const string RedisAlertKey       = "ai:quality:agreement_rate_below_threshold";

    private readonly IAiMetricsReadRepository _metricsRepo;
    private readonly IConnectionMultiplexer?  _redis;

    /// <param name="metricsRepo">Repository for reading recent agreement event counts.</param>
    /// <param name="redis">
    /// Optional Redis multiplexer. When <c>null</c> (development mode), the Redis flag step is
    /// skipped gracefully — only the Serilog warning is emitted.
    /// </param>
    public AgreementRateEvaluator(
        IAiMetricsReadRepository metricsRepo,
        IConnectionMultiplexer? redis = null)
    {
        _metricsRepo = metricsRepo;
        _redis       = redis;
    }

    /// <summary>
    /// Computes the rolling agreement rate and raises an alert if the rate falls below the 98%
    /// threshold with at least 50 verified samples (AIR-Q01).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task EvaluateAsync(CancellationToken ct = default)
    {
        var (totalCount, agreementCount) = await _metricsRepo
            .GetRecentAgreementEventsAsync(WindowSize, ct)
            .ConfigureAwait(false);

        if (totalCount < MinSampleCountForAlert)
        {
            Log.Debug(
                "AgreementRateEvaluator_InsufficientData: sampleCount={SampleCount} — rate evaluation skipped (minimum {Min} required).",
                totalCount, MinSampleCountForAlert);
            return;
        }

        double rate = (double)agreementCount / totalCount;

        if (rate >= AgreementThreshold)
            return;

        Log.Warning(
            "AgreementRateEvaluator_BelowThreshold: agreementRate={Rate:P1} totalSamples={TotalCount} — AI-Human Agreement Rate below 98% target (AIR-Q01).",
            rate, totalCount);

        await TrySetRedisAlertAsync(rate).ConfigureAwait(false);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task TrySetRedisAlertAsync(double rate)
    {
        if (_redis is null)
        {
            Log.Debug("AgreementRateEvaluator: Redis not available — skipping Redis flag (development mode).");
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(
                RedisAlertKey,
                "true",
                TimeSpan.FromHours(1)).ConfigureAwait(false);

            Log.Debug(
                "AgreementRateEvaluator_RedisFlag: key={Key} rate={Rate:P1} — flag set with 1h TTL.",
                RedisAlertKey, rate);
        }
        catch (Exception ex)
        {
            // Metric alert must never interrupt the primary request flow (NFR-018).
            Log.Warning(ex,
                "AgreementRateEvaluator_RedisFailed: could not set Redis alert flag '{Key}'.",
                RedisAlertKey);
        }
    }
}
