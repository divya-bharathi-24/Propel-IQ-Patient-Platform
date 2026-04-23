namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Emergency contact DTO — used in <see cref="PatientProfileDto"/> (read) and
/// <see cref="UpdatePatientProfileDto"/> (write). All fields are optional (PATCH semantics).
/// Validator enforces MaxLength and phone format when provided (US_015, AC-4).
/// </summary>
public sealed record EmergencyContactDto(
    string? Name,
    string? Phone,
    string? Relationship);
