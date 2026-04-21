using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Propel.Modules.Auth.Services;

/// <summary>
/// Manages Redis session keys for the 15-minute server-side session TTL (US_011, NFR-007).
/// Session keys follow the pattern <c>session:{userId}:{deviceId}</c>.
/// All operations gracefully degrade on <see cref="RedisConnectionException"/>; callers must
/// treat a degraded Redis as an authentication failure to maintain security posture (NFR-018).
/// </summary>
public sealed class RedisSessionService : IRedisSessionService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSessionService> _logger;
    private static readonly TimeSpan SessionTtl = TimeSpan.FromSeconds(900); // 15 min — NFR-007

    public RedisSessionService(IConnectionMultiplexer redis, ILogger<RedisSessionService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private static string SessionKey(Guid userId, string deviceId)
        => $"session:{userId}:{deviceId}";

    /// <inheritdoc />
    public async Task SetAsync(Guid userId, string deviceId, string payload, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(SessionKey(userId, deviceId), payload, SessionTtl);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis unavailable — failed to write session for user {UserId}", userId);
            throw; // propagate so the login handler can surface an appropriate error
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync(SessionKey(userId, deviceId));
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis unavailable — treating session as expired for user {UserId}", userId);
            return false; // fail-closed: treat Redis outage as session expired (NFR-018 security-first)
        }
    }

    /// <inheritdoc />
    public async Task ResetTtlAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyExpireAsync(SessionKey(userId, deviceId), SessionTtl);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — TTL slide skipped for user {UserId}", userId);
            // Non-fatal: the existing TTL will still expire; do not throw
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(SessionKey(userId, deviceId));
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis unavailable — failed to delete session for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAllUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Scan for all session keys matching the user (cross-device family invalidation).
            // Upstash Redis supports SCAN; production clusters should use a Lua script for atomicity.
            var server = _redis.GetServers().FirstOrDefault();
            if (server is null) return;

            var pattern = $"session:{userId}:*";
            var db = _redis.GetDatabase();
            var tasks = new List<Task>();

            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                tasks.Add(db.KeyDeleteAsync(key));
            }

            await Task.WhenAll(tasks);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis unavailable — failed to delete all sessions for user {UserId}", userId);
            throw;
        }
    }
}

/// <summary>
/// Abstraction over Redis session management (enables unit-test mocking).
/// </summary>
public interface IRedisSessionService
{
    /// <summary>Creates or overwrites the session key with a 15-minute TTL.</summary>
    Task SetAsync(Guid userId, string deviceId, string payload, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if the session key exists (TTL not yet expired).</summary>
    Task<bool> ExistsAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Resets the session TTL to 15 minutes (sliding window — NFR-007).</summary>
    Task ResetTtlAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Removes the session key for a single device (logout — AC-4).</summary>
    Task DeleteAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes ALL session keys for a user across all devices.
    /// Used during token-family invalidation on reuse detection (AC-3).
    /// </summary>
    Task DeleteAllUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default);
}
