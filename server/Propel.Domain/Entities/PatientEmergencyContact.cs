namespace Propel.Domain.Entities;

/// <summary>
/// Value object representing a patient's emergency contact (US_015, AC-1).
/// Stored as encrypted JSON in the <c>emergency_contact</c> column (PHI — NFR-004, NFR-013).
/// </summary>
public sealed record PatientEmergencyContact(
    string? Name,
    string? Phone,
    string? Relationship);
