using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Records the staff verification decision for a patient's 360-degree profile (AC-3, FR-035).
/// One record per patient — upserted on each successful verify call.
/// The corresponding staff user and timestamp are captured for audit purposes.
/// Table schema is created by task_004 migration.
/// </summary>
public sealed class PatientProfileVerification
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public VerificationStatus Status { get; set; } = VerificationStatus.Pending;

    /// <summary>Staff user ID sourced from JWT claim — never from request body (OWASP A01).</summary>
    public Guid VerifiedBy { get; set; }

    /// <summary>UTC timestamp of the last verification action.</summary>
    public DateTime VerifiedAt { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public User VerifiedByUser { get; set; } = null!;
}
