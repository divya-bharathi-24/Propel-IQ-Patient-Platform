using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for <see cref="RefreshToken"/> persistence.
/// Implementations live in the infrastructure layer (Propel.Api.Gateway).
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Persists a new refresh token record.
    /// </summary>
    Task CreateAsync(RefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a refresh token by its SHA-256 hash. Returns <c>null</c> when not found.
    /// </summary>
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a single refresh token as revoked by setting <see cref="RefreshToken.RevokedAt"/>.
    /// </summary>
    Task RevokeAsync(RefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks ALL tokens belonging to the given <paramref name="familyId"/> as revoked.
    /// Used during token-reuse detection to invalidate the entire token family (AC-3).
    /// </summary>
    Task RevokeTokenFamilyAsync(Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically revokes <paramref name="old"/> and inserts <paramref name="next"/>
    /// within a single database transaction (AC-3 rotation).
    /// </summary>
    Task RotateAsync(RefreshToken old, RefreshToken next, CancellationToken cancellationToken = default);
}
