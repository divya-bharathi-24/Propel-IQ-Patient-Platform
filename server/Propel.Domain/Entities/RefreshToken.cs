namespace Propel.Domain.Entities;

/// <summary>
/// Persisted refresh token used for stateless token rotation (US_011, AC-3).
/// Only the SHA-256 hash of the raw token is stored — never the raw value (OWASP A02, NFR-008).
/// Token families enable reuse-detection: when a revoked token is presented, the entire
/// family is invalidated and a security alert is written to the audit log.
/// Supports both Patient and User (Staff/Admin) authentication via nullable PatientId/UserId.
/// Exactly one of PatientId or UserId must be non-null (enforced via DB CHECK constraint).
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>FK to the patient who owns this token (nullable — set for patient logins). Raw FK — no navigation property.</summary>
    public Guid? PatientId { get; set; }

    /// <summary>FK to the staff/admin user who owns this token (nullable — set for staff/admin logins). Raw FK — no navigation property.</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// SHA-256 hex digest of the raw refresh token.
    /// The raw token is never persisted (OWASP A02).
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// Groups all tokens issued across a single login chain (for reuse-detection / family invalidation).
    /// Set to a new <see cref="Guid"/> on first login; carried forward on each rotation.
    /// </summary>
    public Guid FamilyId { get; set; }

    /// <summary>
    /// Client-supplied device fingerprint used to scope the Redis session key
    /// (<c>session:{userId}:{deviceId}</c>).
    /// </summary>
    public required string DeviceId { get; set; }

    /// <summary>UTC expiry — token is rejected after this point.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp set when the token is rotated or explicitly revoked.
    /// A non-null value means the token has already been consumed (AC-3 reuse detection).
    /// </summary>
    public DateTime? RevokedAt { get; set; }
}
