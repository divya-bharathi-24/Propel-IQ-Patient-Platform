namespace Propel.Modules.Patient.Handlers;

// Internal deserialization shapes for PHI JSON stored in Patient entity columns.
// These are not exposed via the API — the handler maps them to public DTOs.

internal sealed record PatientAddress(
    string? Street,
    string? City,
    string? State,
    string? PostalCode,
    string? Country);

internal sealed record PatientEmergencyContact(
    string? Name,
    string? Phone,
    string? Relationship);

internal sealed record PatientCommunicationPreferences(
    bool? EmailEnabled,
    bool? SmsEnabled,
    bool? PushEnabled);
