using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Propel.Modules.AI.Dtos;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Options;
using Propel.Modules.AI.Queries;
using StackExchange.Redis;

namespace Propel.Modules.AI.Handlers;

/// <summary>
/// Handles <see cref="GetAiOperationalMetricsSummaryQuery"/>: computes aggregate AI operational
/// metrics from the <c>AiOperationalMetrics</c> table and real-time Redis state
/// (EP-010/us_050, AC-4, task_002, AIR-O04).
/// <para>
/// Computation rules:
/// <list type="bullet">
///   <item><description>p95 latency: in-memory sort of last <c>MetricsWindowSize</c> latency records; returns null when fewer than 20 samples.</description></item>
///   <item><description>Token averages: mean of ValueA (prompt) and ValueB (response) over the same window.</description></item>
///   <item><description>Error rate: errors / (errors + latency events) in the last 1 hour.</description></item>
///   <item><description>CB trips: persisted count from <c>AiOperationalMetrics</c> in the last 24 hours (not ephemeral Redis).</description></item>
///   <item><description>CB open state: real-time check of Redis key <c>ai:cb:open</c>.</description></item>
///   <item><description>Active model: reads Redis key <c>ai:config:model_version</c> or settings default (AIR-O03).</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class GetAiOperationalMetricsSummaryQueryHandler
    : IRequestHandler<GetAiOperationalMetricsSummaryQuery, AiOperationalMetricsSummaryResponse>
{
    private const int    MinLatencySamplesForP95 = 20;
    private const string CbOpenRedisKey          = "ai:cb:open";
    private const string ModelVersionRedisKey    = "ai:config:model_version";

    private readonly IAiOperationalMetricsReadRepository   _metricsRepo;
    private readonly IConnectionMultiplexer                _redis;
    private readonly IOptionsMonitor<AiResilienceSettings> _options;
    private readonly ILogger<GetAiOperationalMetricsSummaryQueryHandler> _logger;

    public GetAiOperationalMetricsSummaryQueryHandler(
        IAiOperationalMetricsReadRepository metricsRepo,
        IConnectionMultiplexer redis,
        IOptionsMonitor<AiResilienceSettings> options,
        ILogger<GetAiOperationalMetricsSummaryQueryHandler> logger)
    {
        _metricsRepo = metricsRepo;
        _redis       = redis;
        _options     = options;
        _logger      = logger;
    }

    public async Task<AiOperationalMetricsSummaryResponse> Handle(
        GetAiOperationalMetricsSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var settings   = _options.CurrentValue;
        int windowSize = settings.MetricsWindowSize;

        // Fetch latency + token records and time-window counts concurrently.
        var latencyTask      = _metricsRepo.GetLatencyRecordsAsync(windowSize, cancellationToken);
        var tokenTask        = _metricsRepo.GetTokenConsumptionRecordsAsync(windowSize, cancellationToken);
        var errorCountTask   = _metricsRepo.GetErrorCountAsync(TimeSpan.FromHours(1), cancellationToken);
        var latencyCountTask = _metricsRepo.GetLatencyCountAsync(TimeSpan.FromHours(1), cancellationToken);
        var cbTripsTask      = _metricsRepo.GetCircuitBreakerTripCountAsync(TimeSpan.FromHours(24), cancellationToken);

        await Task.WhenAll(latencyTask, tokenTask, errorCountTask, latencyCountTask, cbTripsTask)
                  .ConfigureAwait(false);

        var latencyRecords = await latencyTask;
        var tokenRecords   = await tokenTask;
        int errorCount     = await errorCountTask;
        int latencyCount1h = await latencyCountTask;
        int cbTrips24h     = await cbTripsTask;

        // ── p95 latency computation (edge case: < 20 samples → null) ──────────
        double? p95LatencyMs = latencyRecords.Count >= MinLatencySamplesForP95
            ? ComputePercentile(latencyRecords.Select(r => (double)(r.ValueA ?? 0m)), 95)
            : null;

        // ── Token consumption averages ────────────────────────────────────────
        double avgPromptTokens   = tokenRecords.Count > 0
            ? tokenRecords.Average(r => (double)(r.ValueA ?? 0m))
            : 0.0;
        double avgResponseTokens = tokenRecords.Count > 0
            ? tokenRecords.Average(r => (double)(r.ValueB ?? 0m))
            : 0.0;

        // ── Error rate = errors / (errors + latency_events) in last 1 hour ───
        int total = errorCount + latencyCount1h;
        double errorRate = total > 0 ? (double)errorCount / total : 0.0;

        // ── Real-time circuit breaker state from Redis ─────────────────────────
        bool cbOpen = await CheckRedisKeyExistsAsync(CbOpenRedisKey, cancellationToken);

        // ── Active model version from Redis (fallback to settings default) ────
        string activeModel = await ReadRedisStringAsync(
            ModelVersionRedisKey,
            settings.DefaultModelVersion,
            cancellationToken);

        // ── Status determination ───────────────────────────────────────────────
        string status = DetermineStatus(p95LatencyMs, cbOpen);

        _logger.LogInformation(
            "GetAiOperationalMetricsSummary: p95={P95} avgPrompt={AvgPrompt} avgResponse={AvgResponse} " +
            "errorRate={ErrorRate} cbTrips24h={CbTrips} cbOpen={CbOpen} model={Model} status={Status}",
            p95LatencyMs, avgPromptTokens, avgResponseTokens,
            errorRate, cbTrips24h, cbOpen, activeModel, status);

        return new AiOperationalMetricsSummaryResponse(
            P95LatencyMs:          p95LatencyMs,
            AvgPromptTokens:       avgPromptTokens,
            AvgResponseTokens:     avgResponseTokens,
            ErrorRate:             errorRate,
            CircuitBreakerTrips24h: cbTrips24h,
            CircuitBreakerOpen:    cbOpen,
            ActiveModelVersion:    activeModel,
            Status:                status
        );
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// In-memory percentile computation via sorted-index lookup.
    /// Formula: <c>sorted[(int)Math.Ceiling(n * percentile / 100.0) - 1]</c>.
    /// </summary>
    private static double ComputePercentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int n      = sorted.Length;
        int index  = (int)Math.Ceiling(n * percentile / 100.0) - 1;
        return sorted[Math.Clamp(index, 0, n - 1)];
    }

    private static string DetermineStatus(double? p95LatencyMs, bool cbOpen)
    {
        if (cbOpen) return "CircuitBreakerOpen";
        if (p95LatencyMs is null) return "InsufficientData";
        return "OK";
    }

    private async Task<bool> CheckRedisKeyExistsAsync(string key, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync(key).ConfigureAwait(false);
        }
        catch
        {
            // Redis unavailability must not fail the dashboard query (NFR-018).
            return false;
        }
    }

    private async Task<string> ReadRedisStringAsync(string key, string fallback, CancellationToken ct)
    {
        try
        {
            var db  = _redis.GetDatabase();
            var val = await db.StringGetAsync(key).ConfigureAwait(false);
            return val.HasValue && !val.IsNullOrEmpty ? (string)val! : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
