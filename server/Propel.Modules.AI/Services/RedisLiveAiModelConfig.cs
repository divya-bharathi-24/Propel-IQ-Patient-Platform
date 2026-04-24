using Microsoft.Extensions.Options;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Options;
using Serilog;
using StackExchange.Redis;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Redis-backed implementation of <see cref="ILiveAiModelConfig"/> with a 60-second in-memory
/// cache (AIR-O03, US_050 AC-3).
/// <para>
/// Reads <c>ai:config:model_version</c> from Redis on cache miss. An operator can update
/// the Redis key and the new value will be picked up within 60 seconds — no application
/// restart required (hot-swap without redeployment).
/// </para>
/// <para>
/// Graceful degradation: when Redis is unavailable (e.g. development mode or transient failure)
/// the cached value or <see cref="AiResilienceSettings.DefaultModelVersion"/> from
/// <c>appsettings.json</c> is returned. Redis errors are swallowed and logged at Warning level
/// so they never disrupt the AI pipeline.
/// </para>
/// </summary>
public sealed class RedisLiveAiModelConfig : ILiveAiModelConfig
{
    private const string RedisKey = "ai:config:model_version";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    // Volatile tuple so reads are thread-safe without locking on the hot path.
    private volatile CacheEntry _cache = new(string.Empty, DateTimeOffset.MinValue);

    private readonly IConnectionMultiplexer? _redis;
    private readonly IOptionsMonitor<AiResilienceSettings> _options;

    public RedisLiveAiModelConfig(
        IConnectionMultiplexer? redis,
        IOptionsMonitor<AiResilienceSettings> options)
    {
        _redis   = redis;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<string> GetModelVersionAsync(CancellationToken ct = default)
    {
        var entry = _cache;

        // Return cached value if still fresh.
        if (!string.IsNullOrEmpty(entry.Version) &&
            DateTimeOffset.UtcNow - entry.CachedAt < CacheTtl)
        {
            return entry.Version;
        }

        string version = await FetchFromRedisAsync(ct);
        _cache = new CacheEntry(version, DateTimeOffset.UtcNow);
        return version;
    }

    private async Task<string> FetchFromRedisAsync(CancellationToken ct)
    {
        if (_redis is null)
            return GetDefault();

        try
        {
            var db = _redis.GetDatabase();
            RedisValue redisValue = await db.StringGetAsync(RedisKey);

            return redisValue.HasValue && !redisValue.IsNullOrEmpty
                ? (string)redisValue!
                : GetDefault();
        }
        catch (Exception ex)
        {
            // Redis unavailability must never disrupt the AI pipeline (NFR-018).
            Log.Warning(ex,
                "RedisLiveAiModelConfig_FetchFailed: Redis read for key={RedisKey} failed — " +
                "falling back to default model version (AIR-O03).",
                RedisKey);

            // Return stale cache entry if available, else appsettings default.
            var stale = _cache;
            return !string.IsNullOrEmpty(stale.Version) ? stale.Version : GetDefault();
        }
    }

    private string GetDefault() => _options.CurrentValue.DefaultModelVersion;

    /// <summary>Immutable cache slot — volatile write is atomic on all supported platforms.</summary>
    private sealed record CacheEntry(string Version, DateTimeOffset CachedAt);
}
