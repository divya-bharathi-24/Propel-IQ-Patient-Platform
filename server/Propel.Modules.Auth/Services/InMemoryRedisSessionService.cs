using Microsoft.Extensions.Logging;

namespace Propel.Modules.Auth.Services;

/// <summary>
/// In-memory Redis session service for local development when Redis is unavailable.
/// WARNING: This is NOT suitable for production. Sessions are stored in memory and
/// will be lost on application restart. Use actual Redis in production.
/// </summary>
public sealed class InMemoryRedisSessionService : IRedisSessionService
{
    private readonly ILogger<InMemoryRedisSessionService> _logger;
    private static readonly Dictionary<string, (string Payload, DateTime ExpiresAt)> _sessions = new();
    private static readonly object _lock = new();
    private static readonly TimeSpan SessionTtl = TimeSpan.FromSeconds(900); // 15 min

    public InMemoryRedisSessionService(ILogger<InMemoryRedisSessionService> logger)
    {
        _logger = logger;
        _logger.LogWarning("Using IN-MEMORY session storage. Sessions will be lost on restart!");
    }

    private static string SessionKey(Guid userId, string deviceId)
        => $"session:{userId}:{deviceId}";

    public Task SetAsync(Guid userId, string deviceId, string payload, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var key = SessionKey(userId, deviceId);
            _sessions[key] = (payload, DateTime.UtcNow.Add(SessionTtl));
            _logger.LogInformation(
                "[SESSION DEBUG] Session SET - Key: {Key}, UserId: {UserId}, DeviceId: {DeviceId}, ExpiresAt: {ExpiresAt}, TotalSessions: {Count}",
                key, userId, deviceId, DateTime.UtcNow.Add(SessionTtl), _sessions.Count);
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var key = SessionKey(userId, deviceId);
            _logger.LogInformation(
                "[SESSION DEBUG] Session EXISTS check - Key: {Key}, UserId: {UserId}, DeviceId: {DeviceId}",
                key, userId, deviceId);

            if (_sessions.TryGetValue(key, out var session))
            {
                if (DateTime.UtcNow < session.ExpiresAt)
                {
                    _logger.LogInformation(
                        "[SESSION DEBUG] Session FOUND and VALID - ExpiresAt: {ExpiresAt}, TimeLeft: {TimeLeft}s",
                        session.ExpiresAt, (session.ExpiresAt - DateTime.UtcNow).TotalSeconds);
                    return Task.FromResult(true);
                }
                // Expired, remove it
                _sessions.Remove(key);
                _logger.LogWarning(
                    "[SESSION DEBUG] Session EXPIRED - Was valid until: {ExpiresAt}",
                    session.ExpiresAt);
            }
            else
            {
                _logger.LogWarning(
                    "[SESSION DEBUG] Session NOT FOUND - Key: {Key}, Available keys: {Keys}",
                    key, string.Join(", ", _sessions.Keys));
            }
            return Task.FromResult(false);
        }
    }

    public Task ResetTtlAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var key = SessionKey(userId, deviceId);
            if (_sessions.TryGetValue(key, out var session))
            {
                _sessions[key] = (session.Payload, DateTime.UtcNow.Add(SessionTtl));
                _logger.LogDebug("Session TTL reset for user {UserId}, device {DeviceId}", userId, deviceId);
            }
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid userId, string deviceId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var key = SessionKey(userId, deviceId);
            _sessions.Remove(key);
            _logger.LogDebug("Session deleted for user {UserId}, device {DeviceId}", userId, deviceId);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAllUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var pattern = $"session:{userId}:";
            var keysToRemove = _sessions.Keys.Where(k => k.StartsWith(pattern)).ToList();
            foreach (var key in keysToRemove)
            {
                _sessions.Remove(key);
            }
            _logger.LogDebug("All sessions deleted for user {UserId}", userId);
        }
        return Task.CompletedTask;
    }
}
