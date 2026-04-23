using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Services;

namespace Propel.Api.Gateway.Infrastructure.Session;

/// <summary>
/// Flushes all active Redis sessions for a given user by delegating to
/// <see cref="IRedisSessionService.DeleteAllUserSessionsAsync"/>, which performs a SCAN+DEL
/// on all keys matching <c>session:{userId}:*</c> (US_045, AC-3, AD-9).
/// <para>
/// In development mode, <see cref="IRedisSessionService"/> resolves to
/// <see cref="InMemoryRedisSessionService"/>, so this operation is a safe no-op (no Redis
/// available) — deactivation still succeeds and the user is logged out on next request
/// when the in-memory session expires.
/// </para>
/// </summary>
public sealed class RedisSessionInvalidationService : ISessionInvalidationService
{
    private readonly IRedisSessionService _sessionService;
    private readonly ILogger<RedisSessionInvalidationService> _logger;

    public RedisSessionInvalidationService(
        IRedisSessionService sessionService,
        ILogger<RedisSessionInvalidationService> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvalidateAllSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _sessionService.DeleteAllUserSessionsAsync(userId, cancellationToken);
            _logger.LogInformation(
                "SessionInvalidation: all sessions flushed for User {UserId}.", userId);
        }
        catch (Exception ex)
        {
            // Log but do not propagate — deactivation must not fail due to a Redis outage.
            // The user's existing JWT will still expire at its natural TTL (15 min) per NFR-007.
            _logger.LogError(ex,
                "SessionInvalidation failed for User {UserId} — Redis may be unavailable. " +
                "User sessions will expire at natural TTL.", userId);
        }
    }
}
