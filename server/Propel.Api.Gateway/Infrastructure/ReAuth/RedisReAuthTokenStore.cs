using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using StackExchange.Redis;

namespace Propel.Api.Gateway.Infrastructure.ReAuth;

/// <summary>
/// Redis-backed single-use re-auth token store (US_046, FR-062, AD-8, OWASP A02).
/// <para>
/// Token lifecycle:
/// <list type="number">
///   <item>Issue: 32-byte CSPRNG token → Base64URL-encoded raw token returned to caller.
///         SHA-256 hash stored as Redis key <c>reauth:{hash}</c> with 5-minute TTL.
///         Only the hash is persisted — raw token never touches Redis.</item>
///   <item>Consume: compute SHA-256 of incoming token → atomic <c>StringGetDeleteAsync</c>.
///         Returns <c>true</c> only if key existed and stored admin ID matches caller.
///         Key is deleted on first call, enforcing single-use semantics.</item>
/// </list>
/// </para>
/// </summary>
public sealed class RedisReAuthTokenStore : IReAuthTokenStore
{
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(5);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisReAuthTokenStore> _logger;

    public RedisReAuthTokenStore(
        IConnectionMultiplexer redis,
        ILogger<RedisReAuthTokenStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> IssueTokenAsync(Guid adminId, CancellationToken cancellationToken = default)
    {
        // Generate a cryptographically secure 32-byte token (256-bit entropy).
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        string rawToken = Convert.ToBase64String(rawBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('='); // Base64URL — safe for HTTP headers

        string redisKey = BuildKey(rawToken);
        string storedValue = adminId.ToString();

        var db = _redis.GetDatabase();
        bool set = await db.StringSetAsync(
            redisKey,
            storedValue,
            TokenTtl,
            When.NotExists); // NX: prevents accidental collision overwrites

        if (!set)
        {
            // Collision is astronomically unlikely with 256-bit entropy but we guard anyway.
            _logger.LogWarning(
                "ReAuthTokenStore.IssueTokenAsync: collision or duplicate key for Admin {AdminId}. " +
                "Regenerating.", adminId);
            return await IssueTokenAsync(adminId, cancellationToken);
        }

        _logger.LogInformation(
            "ReAuthTokenStore: token issued for Admin {AdminId} (TTL 5 min).", adminId);

        return rawToken;
    }

    /// <inheritdoc />
    public async Task<bool> ConsumeTokenAsync(
        string token,
        Guid expectedAdminId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        string redisKey = BuildKey(token);
        var db = _redis.GetDatabase();

        // Atomic GET+DEL: single-use enforced — key is removed regardless of admin ID match.
        RedisValue storedValue = await db.StringGetDeleteAsync(redisKey);

        if (storedValue.IsNullOrEmpty)
        {
            _logger.LogWarning(
                "ReAuthTokenStore.ConsumeTokenAsync: token not found or already consumed " +
                "(expectedAdmin={AdminId}).", expectedAdminId);
            return false;
        }

        if (!Guid.TryParse((string?)storedValue, out Guid storedAdminId) || storedAdminId != expectedAdminId)
        {
            _logger.LogWarning(
                "ReAuthTokenStore.ConsumeTokenAsync: admin ID mismatch — " +
                "stored={StoredAdmin}, expected={ExpectedAdmin}.",
                storedValue, expectedAdminId);
            return false;
        }

        _logger.LogInformation(
            "ReAuthTokenStore: token consumed for Admin {AdminId}.", expectedAdminId);

        return true;
    }

    /// <summary>
    /// Derives the Redis key from the raw token using SHA-256.
    /// The raw token is never stored in Redis — only its hash (OWASP A02).
    /// </summary>
    private static string BuildKey(string rawToken)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return $"reauth:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
