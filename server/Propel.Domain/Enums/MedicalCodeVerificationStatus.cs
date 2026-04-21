namespace Propel.Domain.Enums;

/// <summary>
/// Tracks the staff review state of an AI-suggested ICD-10 or CPT medical code.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum MedicalCodeVerificationStatus
{
    Pending,
    Accepted,
    Rejected,
    Modified
}
