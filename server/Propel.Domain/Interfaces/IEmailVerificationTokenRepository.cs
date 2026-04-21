using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for <see cref="EmailVerificationToken"/> persistence.
/// The raw token is never stored — only its SHA-256 hash is persisted (NFR-008).
/// </summary>
public interface IEmailVerificationTokenRepository
{
    /// <summary>
    /// Finds an active verification token by its SHA-256 hex hash.
    /// Returns <c>null</c> when no record matches.
    /// </summary>
    Task<EmailVerificationToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Creates a new token record.</summary>
    Task CreateAsync(EmailVerificationToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks all unused tokens for the given patient as consumed
    /// (sets <c>UsedAt = UtcNow</c>) to prevent reuse on resend.
    /// </summary>
    Task InvalidatePendingTokensAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <c>token.UsedAt = UtcNow</c> on the tracked entity.
    /// This is an in-memory mutation; the caller must call <see cref="IPatientRepository.MarkEmailVerifiedAsync"/>
    /// (which triggers <c>SaveChangesAsync</c>) to persist both changes atomically.
    /// </summary>
    void MarkAsUsed(EmailVerificationToken token);
}
