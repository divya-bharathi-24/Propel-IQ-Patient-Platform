namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Write DTO accepted by <c>PATCH /api/patients/me</c> (US_015, AC-2, AC-3).
/// Contains ONLY non-locked fields — locked fields (Name, DateOfBirth, BiologicalSex)
/// are intentionally absent so they can never be mutated via this endpoint (AC-3).
/// All fields are optional per PATCH semantics; null values are not applied to the entity.
/// </summary>
public sealed record UpdatePatientProfileDto(
    string? Phone,
    AddressDto? Address,
    EmergencyContactDto? EmergencyContact,
    CommunicationPreferencesDto? CommunicationPreferences,
    string? InsurerName,
    string? MemberId,
    string? GroupNumber);
