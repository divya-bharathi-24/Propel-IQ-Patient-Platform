namespace Propel.Domain.Entities;

/// <summary>
/// Represents a single-use email verification token issued during patient registration.
/// The raw token is never persisted — only the SHA-256 hash (<see cref="TokenHash"/>) is stored (NFR-008).
/// </summary>
public sealed class EmailVerificationToken
{
    public Guid Id { get; set; }

    /// <summary>FK to the patient this token belongs to.</summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// SHA-256 hash of the raw 32-byte URL-safe base64 token.
    /// Stored as a 64-character hex string. Never store the raw token.
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>UTC expiry timestamp — token is invalid after this point (HTTP 410).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// UTC timestamp set when the token is consumed. Non-null means the token has been used (HTTP 409).
    /// </summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Patient? Patient { get; set; }
}
