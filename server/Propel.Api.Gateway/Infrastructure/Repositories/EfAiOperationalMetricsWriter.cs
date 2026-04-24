using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Modules.AI.Interfaces;
using Serilog;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// INSERT-only EF Core implementation of <see cref="IAiOperationalMetricsWriter"/>
/// (EP-010/us_050, task_002 — API).
/// <para>
/// All four write methods issue a single INSERT via <c>Add</c> + <c>SaveChangesAsync</c>.
/// Exceptions are caught and logged via Serilog Error — they are never propagated to callers
/// (fire-and-forget contract, NFR-018). Metric write failures must not affect the primary
/// clinical write path.
/// </para>
/// <para>
/// Registered as <c>Scoped</c> — shares <see cref="AppDbContext"/> lifetime with the
/// HTTP request. Callers use the discard pattern (<c>_ = writer.RecordXxx()</c>) so the
/// metrics write races concurrently without blocking the primary response (AD-7).
/// </para>
/// </summary>
public sealed class EfAiOperationalMetricsWriter : IAiOperationalMetricsWriter
{
    private readonly AppDbContext _context;

    public EfAiOperationalMetricsWriter(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task RecordTokenConsumptionAsync(
        Guid sessionId,
        string modelVersion,
        int promptTokens,
        int responseTokens)
    {
        try
        {
            var entity = new AiOperationalMetric
            {
                Id           = Guid.NewGuid(),
                MetricType   = AiOperationalMetricType.TokenConsumption,
                SessionId    = sessionId,
                ModelVersion = modelVersion,
                ValueA       = promptTokens,
                ValueB       = responseTokens,
                RecordedAt   = DateTimeOffset.UtcNow
            };
            _context.AiOperationalMetrics.Add(entity);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "EfAiOperationalMetricsWriter_TokenConsumption: Failed to record token consumption — sessionId={SessionId} model={ModelVersion}",
                sessionId, modelVersion);
            // Swallow — metrics writes must not affect primary clinical write path (NFR-018).
        }
    }

    /// <inheritdoc />
    public async Task RecordLatencyAsync(
        Guid sessionId,
        string modelVersion,
        long latencyMs)
    {
        try
        {
            var entity = new AiOperationalMetric
            {
                Id           = Guid.NewGuid(),
                MetricType   = AiOperationalMetricType.Latency,
                SessionId    = sessionId,
                ModelVersion = modelVersion,
                ValueA       = latencyMs,
                RecordedAt   = DateTimeOffset.UtcNow
            };
            _context.AiOperationalMetrics.Add(entity);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "EfAiOperationalMetricsWriter_Latency: Failed to record latency — sessionId={SessionId} model={ModelVersion} latencyMs={LatencyMs}",
                sessionId, modelVersion, latencyMs);
        }
    }

    /// <inheritdoc />
    public async Task RecordProviderErrorAsync(
        Guid sessionId,
        string modelVersion,
        string errorType)
    {
        try
        {
            var entity = new AiOperationalMetric
            {
                Id           = Guid.NewGuid(),
                MetricType   = AiOperationalMetricType.ProviderError,
                SessionId    = sessionId,
                ModelVersion = modelVersion,
                Metadata     = errorType,
                RecordedAt   = DateTimeOffset.UtcNow
            };
            _context.AiOperationalMetrics.Add(entity);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "EfAiOperationalMetricsWriter_ProviderError: Failed to record provider error — sessionId={SessionId} model={ModelVersion} errorType={ErrorType}",
                sessionId, modelVersion, errorType);
        }
    }

    /// <inheritdoc />
    public async Task RecordCircuitBreakerTripAsync(
        string modelVersion,
        int tripCountThisHour,
        TimeSpan openDuration)
    {
        try
        {
            var entity = new AiOperationalMetric
            {
                Id           = Guid.NewGuid(),
                MetricType   = AiOperationalMetricType.CircuitBreakerTrip,
                SessionId    = null,  // CB trips are cross-session events
                ModelVersion = modelVersion,
                ValueA       = tripCountThisHour,
                Metadata     = ((int)openDuration.TotalMinutes).ToString(),
                RecordedAt   = DateTimeOffset.UtcNow
            };
            _context.AiOperationalMetrics.Add(entity);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "EfAiOperationalMetricsWriter_CircuitBreakerTrip: Failed to record CB trip — model={ModelVersion} tripCount={TripCount}",
                modelVersion, tripCountThisHour);
        }
    }
}
