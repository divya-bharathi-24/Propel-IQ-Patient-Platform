namespace Propel.Modules.Calendar.Interfaces;

/// <summary>
/// Stores and retrieves one-time PKCE OAuth state payloads with a short TTL (10 minutes).
/// Dev implementation uses a concurrent in-memory dictionary;
/// prod implementation uses Redis SETEX + DEL (OWASP A07 — one-time consumption).
/// </summary>
public interface IOAuthStateService
{
    /// <summary>Persist <paramref name="payload"/> under <paramref name="stateKey"/> for 10 minutes.</summary>
    Task SetAsync(string stateKey, string payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve and atomically delete the payload stored under <paramref name="stateKey"/>.
    /// Returns <c>null</c> if the key does not exist or has already been consumed (one-time use).
    /// </summary>
    Task<string?> GetAndDeleteAsync(string stateKey, CancellationToken cancellationToken = default);
}
