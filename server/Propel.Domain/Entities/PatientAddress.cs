namespace Propel.Domain.Entities;

/// <summary>
/// Value object representing a patient's mailing/home address (US_015, AC-1).
/// Stored as encrypted JSON in the <c>address</c> column (PHI — NFR-004, NFR-013).
/// </summary>
public sealed record PatientAddress(
    string? Street,
    string? City,
    string? State,
    string? PostalCode,
    string? Country);
