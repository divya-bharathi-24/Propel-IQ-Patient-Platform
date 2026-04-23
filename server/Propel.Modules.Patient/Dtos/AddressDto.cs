namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Address DTO — used in <see cref="PatientProfileDto"/> (read) and
/// <see cref="UpdatePatientProfileDto"/> (write). All fields are optional (PATCH semantics).
/// Validator enforces MaxLength(200) on each field when provided (US_015, AC-4).
/// </summary>
public sealed record AddressDto(
    string? Street,
    string? City,
    string? State,
    string? PostalCode,
    string? Country);
