using System.Text.Json;
using Microsoft.Extensions.Logging;
using Propel.Modules.Appointment.Dtos;
using Propel.Modules.Appointment.Infrastructure;
using StackExchange.Redis;

namespace Propel.Api.Gateway.Infrastructure.Cache;

/// <summary>
/// Production <see cref="ISlotCacheService"/> implementation backed by Upstash Redis via
/// StackExchange.Redis (US_018, AC-1, AC-2, AC-3, NFR-018, NFR-020).
/// <para>
/// Cache key: <c>slots:{specialtyId}:{date:yyyy-MM-dd}</c>. TTL: 5 seconds (NFR-020).
/// </para>
/// <para>
/// All methods catch any <see cref="Exception"/> from the Redis layer (including
/// <see cref="RedisException"/>, <see cref="RedisConnectionException"/>, and
/// <see cref="InvalidOperationException"/> from development stubs). On failure:
/// <list type="bullet">
///   <item><c>GetAsync</c> returns <c>null</c> and logs <c>Warning "SlotCache_Miss"</c> (AC-3).</item>
///   <item><c>SetAsync</c> and <c>InvalidateAsync</c> swallow the exception and log a Warning
///         so that cache failures never block user requests (NFR-018).</item>
/// </list>
/// </para>
/// </summary>
public sealed class RedisSlotCacheService : ISlotCacheService
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisSlotCacheService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RedisSlotCacheService(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisSlotCacheService> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
    }

    private static string BuildKey(string specialtyId, DateOnly date) =>
        $"slots:{specialtyId}:{date:yyyy-MM-dd}";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SlotDto>?> GetAsync(
        string specialtyId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var key = BuildKey(specialtyId, date);
            var value = await db.StringGetAsync(key);

            if (value.IsNullOrEmpty)
                return null;

            return JsonSerializer.Deserialize<IReadOnlyList<SlotDto>>((string)value!, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "SlotCache_Miss: {SpecialtyId} {Date} {Reason}",
                specialtyId, date, ex.Message);

            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync(
        string specialtyId,
        DateOnly date,
        IReadOnlyList<SlotDto> slots,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var key = BuildKey(specialtyId, date);
            var json = JsonSerializer.Serialize(slots, JsonOptions);
            await db.StringSetAsync(key, json, ttl);
        }
        catch (Exception ex)
        {
            // Graceful degradation: a failed cache write must not fail the response (NFR-018).
            _logger.LogWarning(
                "SlotCache_SetFailed: SpecialtyId={SpecialtyId} Date={Date} Reason={Reason}",
                specialtyId, date, ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task InvalidateAsync(
        string specialtyId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            var key = BuildKey(specialtyId, date);
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "SlotCache_InvalidateFailed: SpecialtyId={SpecialtyId} Date={Date} Reason={Reason}",
                specialtyId, date, ex.Message);
        }
    }
}
