namespace Propel.Domain.Entities;

/// <summary>
/// One-time credential setup token issued when an admin creates a Staff or Admin account.
/// The raw token is never persisted — only its SHA-256 hex hash (<see cref="TokenHash"/>)
/// is stored, matching the same pattern used by <see cref="EmailVerificationToken"/> (NFR-008).
/// </summary>
public sealed class CredentialSetupToken
{
    public Guid Id { get; set; }

    /// <summary>FK to the <see cref="User"/> this token belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// SHA-256 hash of the raw 32-byte URL-safe base64 token.
    /// Stored as a 64-character hex string. Raw token is never persisted.
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>UTC expiry — 72 hours after issuance (3-day invite window).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// UTC timestamp when the token was consumed. Non-null means already used (HTTP 409).
    /// </summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    // Navigation
    public User? User { get; set; }
}
