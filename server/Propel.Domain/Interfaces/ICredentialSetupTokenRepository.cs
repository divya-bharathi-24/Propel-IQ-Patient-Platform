using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for <see cref="CredentialSetupToken"/> persistence.
/// The raw token is never stored — only its SHA-256 hash is persisted (NFR-008),
/// mirroring the pattern used by <see cref="IEmailVerificationTokenRepository"/>.
/// </summary>
public interface ICredentialSetupTokenRepository
{
    /// <summary>
    /// Creates a new credential setup token record and returns the persisted entity.
    /// </summary>
    Task CreateAsync(CredentialSetupToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a credential setup token by its SHA-256 hex hash.
    /// Returns <c>null</c> when no record matches.
    /// </summary>
    Task<CredentialSetupToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks all unused tokens for the given user as consumed (<c>UsedAt = UtcNow</c>).
    /// Called during resend-invite to invalidate stale tokens before issuing a new one.
    /// </summary>
    Task InvalidatePendingTokensAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <c>token.UsedAt = UtcNow</c> on the tracked entity (in-memory mutation).
    /// The caller must call <see cref="IUserRepository.UpdatePasswordHashAsync"/> to persist
    /// both changes atomically via <c>SaveChangesAsync</c>.
    /// </summary>
    void MarkAsUsed(CredentialSetupToken token);
}
