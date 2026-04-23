namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Communication preferences DTO — used in <see cref="PatientProfileDto"/> (read) and
/// <see cref="UpdatePatientProfileDto"/> (write). All fields are optional (PATCH semantics).
/// Non-PHI — stored as plain JSONB (US_015, AC-1).
/// </summary>
public sealed record CommunicationPreferencesDto(
    bool? EmailEnabled,
    bool? SmsEnabled,
    bool? PushEnabled);
