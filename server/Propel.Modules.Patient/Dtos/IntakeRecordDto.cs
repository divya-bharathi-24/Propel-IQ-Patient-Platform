using System.Text.Json;

namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Read DTO returned by <c>GET /api/intake/{appointmentId}</c> (US_017, AC-1).
/// Includes all JSONB intake fields, timestamps, and the Base64-encoded <c>xmin</c> row version
/// used as an ETag for optimistic concurrency on subsequent PUT requests (AC-2).
/// </summary>
public sealed record IntakeRecordDto(
    Guid Id,
    Guid PatientId,
    Guid AppointmentId,
    string Source,
    JsonDocument Demographics,
    JsonDocument MedicalHistory,
    JsonDocument Symptoms,
    JsonDocument Medications,
    DateTime? CompletedAt,
    DateTime? LastModifiedAt,
    string ETag);
