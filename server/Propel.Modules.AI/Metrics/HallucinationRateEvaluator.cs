using Propel.Modules.AI.Interfaces;
using Serilog;
using StackExchange.Redis;

namespace Propel.Modules.AI.Metrics;

/// <summary>
/// Evaluates the rolling AI hallucination rate over the most recent staff-verified field samples
/// and raises a Serilog critical alert + Redis flag when the rate exceeds 2% (us_048, AC-3, AIR-Q04).
/// <para>
/// Rate computation:
/// <c>hallucinationRate = hallucinatedCount / totalVerified</c> over a window of 200 samples.
/// </para>
/// <para>
/// Guards:
/// <list type="bullet">
///   <item><description>If <c>totalVerified &lt; 50</c>: insufficient ground truth — returns without alert;
///     the rate is displayed as "Insufficient data" in the metrics dashboard (edge case specification).</description></item>
///   <item><description>If <c>hallucinationRate &gt; 0.02</c> and <c>totalVerified ≥ 50</c>: log Serilog
///     critical/fatal event and set Redis key <c>ai:quality:hallucination_rate_above_threshold</c>
///     with a 1-hour TTL.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class HallucinationRateEvaluator
{
    private const int    WindowSize             = 200;
    private const int    MinSampleCountForAlert = 50;
    private const double HallucinationThreshold = 0.02;
    private const string RedisAlertKey          = "ai:quality:hallucination_rate_above_threshold";

    private readonly IAiMetricsReadRepository _metricsRepo;
    private readonly IConnectionMultiplexer?  _redis;

    /// <param name="metricsRepo">Repository for reading recent verified sample counts.</param>
    /// <param name="redis">
    /// Optional Redis multiplexer. When <c>null</c> (development mode), the Redis flag step is
    /// skipped gracefully — only the Serilog alert is emitted.
    /// </param>
    public HallucinationRateEvaluator(
        IAiMetricsReadRepository metricsRepo,
        IConnectionMultiplexer? redis = null)
    {
        _metricsRepo = metricsRepo;
        _redis       = redis;
    }

    /// <summary>
    /// Computes the rolling hallucination rate and raises a critical-level Serilog alert +
    /// Redis flag when the rate exceeds 2% with at least 50 verified samples (AIR-Q04).
    /// Returns without action when insufficient ground truth is available (edge case).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task EvaluateAsync(CancellationToken ct = default)
    {
        var (totalVerified, hallucinatedCount) = await _metricsRepo
            .GetRecentVerifiedSamplesAsync(WindowSize, ct)
            .ConfigureAwait(false);

        if (totalVerified < MinSampleCountForAlert)
        {
            Log.Debug(
                "HallucinationRateEvaluator_InsufficientData: verifiedSamples={TotalVerified} — hallucination rate evaluation skipped (minimum {Min} required, rate = Insufficient data).",
                totalVerified, MinSampleCountForAlert);
            return;
        }

        double hallucinationRate = (double)hallucinatedCount / totalVerified;

        if (hallucinationRate <= HallucinationThreshold)
            return;

        // Use Fatal-level as the closest Serilog equivalent to the "critical" severity required by AIR-Q04.
        Log.Fatal(
            "HallucinationRateEvaluator_ThresholdExceeded: hallucinationRate={Rate:P1} hallucinatedCount={HallucinatedCount} totalVerified={TotalVerified} — hallucination rate exceeds 2% threshold; model review required (AIR-Q04).",
            hallucinationRate, hallucinatedCount, totalVerified);

        await TrySetRedisAlertAsync(hallucinationRate).ConfigureAwait(false);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task TrySetRedisAlertAsync(double rate)
    {
        if (_redis is null)
        {
            Log.Debug("HallucinationRateEvaluator: Redis not available — skipping Redis flag (development mode).");
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
                "HallucinationRateEvaluator_RedisFlag: key={Key} rate={Rate:P1} — flag set with 1h TTL.",
                RedisAlertKey, rate);
        }
        catch (Exception ex)
        {
            // Alert flag failure must never interrupt the primary request flow (NFR-018).
            Log.Warning(ex,
                "HallucinationRateEvaluator_RedisFailed: could not set Redis alert flag '{Key}'.",
                RedisAlertKey);
        }
    }
}
