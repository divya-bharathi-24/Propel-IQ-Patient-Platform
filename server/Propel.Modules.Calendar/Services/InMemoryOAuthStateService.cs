using System.Collections.Concurrent;
using Propel.Modules.Calendar.Interfaces;

namespace Propel.Modules.Calendar.Services;

/// <summary>
/// In-memory OAuth state service for local development (Redis unavailable).
/// Stores PKCE state payloads in a <see cref="ConcurrentDictionary"/> with TTL tracked manually.
/// NOT suitable for production (state is lost on restart, not distributed).
/// </summary>
public sealed class InMemoryOAuthStateService : IOAuthStateService
{
    private static readonly ConcurrentDictionary<string, (string Payload, DateTime ExpiresAt)> _store = new();
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    public Task SetAsync(string stateKey, string payload, CancellationToken cancellationToken = default)
    {
        _store[stateKey] = (payload, DateTime.UtcNow.Add(StateTtl));
        return Task.CompletedTask;
    }

    public Task<string?> GetAndDeleteAsync(string stateKey, CancellationToken cancellationToken = default)
    {
        if (_store.TryRemove(stateKey, out var entry))
        {
            if (DateTime.UtcNow < entry.ExpiresAt)
                return Task.FromResult<string?>(entry.Payload);
        }
        return Task.FromResult<string?>(null);
    }
}
