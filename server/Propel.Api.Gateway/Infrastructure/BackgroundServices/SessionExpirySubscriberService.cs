using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Modules.Auth.Audit;
using StackExchange.Redis;

namespace Propel.Api.Gateway.Infrastructure.BackgroundServices;

/// <summary>
/// Subscribes to Redis keyspace notifications for expired keys and writes a
/// <see cref="AuthAuditActions.SessionTimeout"/> audit entry when a <c>session:*</c> key expires
/// (US_013, AC-3, NFR-007).
/// <para>
/// Implementation notes:
/// <list type="bullet">
///   <item>Requires <c>notify-keyspace-events KEx</c> (Key events + eXpiry) on the Redis server.
///         On startup this service attempts to set the config; Upstash free-tier may ignore the
///         command silently — the <see cref="SessionAliveMiddleware"/> provides a fallback path
///         for that scenario (AC-3 dual-path guarantee).</item>
///   <item>Session key pattern: <c>session:{userId:Guid}:{deviceId}</c>.</item>
///   <item>Creates a fresh DI scope per event so that <see cref="AuditLogService"/> and its
///         underlying <see cref="IDbContextFactory{TContext}"/>-based repository are fully
///         independent of any request scope.</item>
///   <item>Audit write must complete within 5 seconds of the expiry event (AC-3 SLA).</item>
/// </list>
/// </para>
/// </summary>
public sealed class SessionExpirySubscriberService : IHostedService
{
    // Matches session:{guid}:{deviceId} — captures userId group 1, deviceId group 2
    private static readonly Regex SessionKeyPattern =
        new(@"^session:([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}):(.+)$",
            RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private const string KeyspaceChannel = "__keyevent@0__:expired";

    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionExpirySubscriberService> _logger;

    private ISubscriber? _subscriber;

    public SessionExpirySubscriberService(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        ILogger<SessionExpirySubscriberService> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // NFR-018: Graceful degradation when Redis is unavailable
        try
        {
            // Verify Redis connection is available before attempting to subscribe
            if (!_redis.IsConnected)
            {
                _logger.LogWarning(
                    "Redis connection unavailable - SessionExpirySubscriber disabled. " +
                    "SessionTimeout audit events will rely on SessionAliveMiddleware fallback path only.");
                return;
            }

            // Attempt to enable keyspace notifications (KEx = Key events + eXpiry events).
            // Upstash free-tier may silently ignore this; the SessionAliveMiddleware acts as fallback.
            try
            {
                var server = _redis.GetServer(_redis.GetEndPoints()[0]);
                await server.ConfigSetAsync("notify-keyspace-events", "KEx");
                _logger.LogInformation("Redis keyspace notifications enabled (KEx).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not enable Redis keyspace notifications — Upstash free-tier may not support CONFIG SET. " +
                    "SessionTimeout audit events will rely on the SessionAliveMiddleware fallback path.");
            }

            _subscriber = _redis.GetSubscriber();

            await _subscriber.SubscribeAsync(
                RedisChannel.Literal(KeyspaceChannel),
                OnKeyExpired);

            _logger.LogInformation(
                "SessionExpirySubscriberService started, listening on channel {Channel}", KeyspaceChannel);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex,
                "Redis connection failed during SessionExpirySubscriber startup - service disabled. " +
                "SessionTimeout audit events will rely on SessionAliveMiddleware fallback path only. " +
                "This is expected when Redis is unavailable (NFR-018 graceful degradation).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error starting SessionExpirySubscriberService - service disabled.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null)
        {
            await _subscriber.UnsubscribeAsync(RedisChannel.Literal(KeyspaceChannel));
            _logger.LogInformation("SessionExpirySubscriberService stopped.");
        }
    }

    private void OnKeyExpired(RedisChannel channel, RedisValue key)
    {
        string keyStr = key.ToString();

        var match = SessionKeyPattern.Match(keyStr);
        if (!match.Success)
            return; // not a session key — ignore

        if (!Guid.TryParse(match.Groups[1].Value, out Guid userId))
        {
            _logger.LogWarning("SessionExpiry: could not parse userId from key {Key}", keyStr);
            return;
        }

        // Fire-and-forget with dedicated scope — must complete within 5 s (AC-3 SLA)
        _ = WriteSessionTimeoutAuditAsync(userId);
    }

    private async Task WriteSessionTimeoutAuditAsync(Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var auditLog = scope.ServiceProvider.GetRequiredService<AuditLogService>();

        try
        {
            await auditLog.AppendAsync(new AuditLog
            {
                Id         = Guid.NewGuid(),
                UserId     = userId,
                Action     = AuthAuditActions.SessionTimeout,
                EntityType = "User",
                EntityId   = userId,
                Timestamp  = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            // AuditLogService already handles retries and LogCritical; this catch is a
            // final safety net to prevent an unobserved task exception from crashing the host.
            _logger.LogError(ex,
                "Unhandled error in SessionTimeout audit write for user {UserId}", userId);
        }
    }
}
