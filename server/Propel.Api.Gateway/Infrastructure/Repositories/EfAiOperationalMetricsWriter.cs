using Microsoft.EntityFrameworkCore;
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
/// Uses <see cref="IDbContextFactory{AppDbContext}"/> to create an isolated DbContext per write,
/// preventing EF Core concurrency errors when callers use the fire-and-forget discard pattern
/// (<c>_ = writer.RecordXxx()</c>) alongside concurrent primary-path DbContext operations (AD-7, NFR-018).
/// </para>
/// </summary>
public sealed class EfAiOperationalMetricsWriter : IAiOperationalMetricsWriter
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EfAiOperationalMetricsWriter(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
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
            await using var ctx = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
            ctx.AiOperationalMetrics.Add(new AiOperationalMetric
            {
                Id           = Guid.NewGuid(),
                MetricType   = AiOperationalMetricType.TokenConsumption,
                SessionId    = sessionId,
                ModelVersion = modelVersion,
                ValueA       = promptTokens,
                ValueB       = responseTokens,
                RecordedAt   = DateTimeOffset.UtcNow
            });
            await ctx.SaveChangesAsync().ConfigureAwait(false);
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
            await using var ctx = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
            ctx.AiOperationalMetrics.Add(new AiOperationalMetric
            {
                Id           = Guid.NewGuid(),
                MetricType   = AiOperationalMetricType.Latency,
                SessionId    = sessionId,
                ModelVersion = modelVersion,
                ValueA       = latencyMs,
                RecordedAt   = DateTimeOffset.UtcNow
            });
            await ctx.SaveChangesAsync().ConfigureAwait(false);
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
            await using var ctx = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
            ctx.AiOperationalMetrics.Add(new AiOperationalMetric
            {
                Id           = Guid.NewGuid(),
                MetricType   = AiOperationalMetricType.ProviderError,
                SessionId    = sessionId,
                ModelVersion = modelVersion,
                Metadata     = errorType,
                RecordedAt   = DateTimeOffset.UtcNow
            });
            await ctx.SaveChangesAsync().ConfigureAwait(false);
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
            await using var ctx = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
            ctx.AiOperationalMetrics.Add(new AiOperationalMetric
            {
                Id           = Guid.NewGuid(),
                MetricType   = AiOperationalMetricType.CircuitBreakerTrip,
                SessionId    = null,  // CB trips are cross-session events
                ModelVersion = modelVersion,
                ValueA       = tripCountThisHour,
                Metadata     = ((int)openDuration.TotalMinutes).ToString(),
                RecordedAt   = DateTimeOffset.UtcNow
            });
            await ctx.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "EfAiOperationalMetricsWriter_CircuitBreakerTrip: Failed to record CB trip — model={ModelVersion} tripCount={TripCount}",
                modelVersion, tripCountThisHour);
        }
    }
}
