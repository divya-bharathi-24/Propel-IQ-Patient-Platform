namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Read DTO returned by <c>GET /api/patients/me</c> (US_015, AC-1).
/// Includes all fields — both locked (Name, DateOfBirth, BiologicalSex) and non-locked.
/// PHI fields (Name, Phone, Address) are decrypted by the PHI value converters in EF Core
/// before being mapped to this DTO (NFR-004, NFR-013).
/// </summary>
public sealed record PatientProfileDto(
    Guid Id,
    string Name,
    DateOnly DateOfBirth,
    string? BiologicalSex,
    string Email,
    string? Phone,
    AddressDto? Address,
    EmergencyContactDto? EmergencyContact,
    CommunicationPreferencesDto? CommunicationPreferences,
    string? InsurerName,
    string? MemberId,
    string? GroupNumber);
