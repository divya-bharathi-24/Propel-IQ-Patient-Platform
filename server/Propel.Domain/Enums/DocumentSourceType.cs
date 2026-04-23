namespace Propel.Domain.Enums;

/// <summary>
/// Distinguishes the origin of a clinical document upload.
/// Used to surface the "Staff Upload" badge on document history (AC-2, FR-044)
/// and to enforce soft-delete eligibility (staff-uploaded notes only, within 24 hours).
/// Stored as VARCHAR(50) in the database for human-readable audit logs.
/// </summary>
public enum DocumentSourceType
{
    /// <summary>Patient self-uploaded via <c>POST /api/documents/upload</c> (US_038, FR-041).</summary>
    PatientUpload,

    /// <summary>Staff-uploaded post-visit clinical note via <c>POST /api/staff/documents/upload</c> (US_039, FR-044).</summary>
    StaffUpload
}
