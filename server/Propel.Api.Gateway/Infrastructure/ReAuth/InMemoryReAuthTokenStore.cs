using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.ReAuth;

/// <summary>
/// In-memory re-auth token store for development mode (Redis disabled).
/// Functionally equivalent to <see cref="RedisReAuthTokenStore"/> but uses a
/// <see cref="ConcurrentDictionary"/> with manual expiry tracking.
/// <para>
/// Not for production use — tokens survive process recycles and are not distributed.
/// </para>
/// </summary>
public sealed class InMemoryReAuthTokenStore : IReAuthTokenStore
{
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (Guid AdminId, DateTimeOffset Expires)> _store = new();
    private readonly ILogger<InMemoryReAuthTokenStore> _logger;

    public InMemoryReAuthTokenStore(ILogger<InMemoryReAuthTokenStore> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<string> IssueTokenAsync(Guid adminId, CancellationToken cancellationToken = default)
    {
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        string rawToken = Convert.ToBase64String(rawBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        string key = BuildKey(rawToken);
        _store[key] = (adminId, DateTimeOffset.UtcNow.Add(TokenTtl));

        _logger.LogInformation(
            "InMemoryReAuthTokenStore: token issued for Admin {AdminId} (TTL 5 min).", adminId);

        return Task.FromResult(rawToken);
    }

    /// <inheritdoc />
    public Task<bool> ConsumeTokenAsync(
        string token,
        Guid expectedAdminId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult(false);

        string key = BuildKey(token);

        // Atomic remove — single-use enforced
        if (!_store.TryRemove(key, out var entry))
        {
            _logger.LogWarning(
                "InMemoryReAuthTokenStore: token not found or already consumed (expectedAdmin={AdminId}).",
                expectedAdminId);
            return Task.FromResult(false);
        }

        if (entry.Expires < DateTimeOffset.UtcNow)
        {
            _logger.LogWarning(
                "InMemoryReAuthTokenStore: token expired for Admin {AdminId}.", expectedAdminId);
            return Task.FromResult(false);
        }

        if (entry.AdminId != expectedAdminId)
        {
            _logger.LogWarning(
                "InMemoryReAuthTokenStore: admin ID mismatch — stored={StoredAdmin}, expected={ExpectedAdmin}.",
                entry.AdminId, expectedAdminId);
            return Task.FromResult(false);
        }

        _logger.LogInformation(
            "InMemoryReAuthTokenStore: token consumed for Admin {AdminId}.", expectedAdminId);

        return Task.FromResult(true);
    }

    private static string BuildKey(string rawToken)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
