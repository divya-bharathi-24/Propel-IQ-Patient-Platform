using System.Text.Json;

namespace Propel.Modules.Patient.Dtos;

/// <summary>
/// Represents the JSONB section payloads of a single intake record (manual draft or AI-extracted).
/// <c>null</c> values indicate the section was not yet collected (US_029).
/// </summary>
public sealed record IntakeDraftDataDto(
    JsonDocument? Demographics,
    JsonDocument? MedicalHistory,
    JsonDocument? Symptoms,
    JsonDocument? Medications);

/// <summary>
/// Response DTO for <c>GET /api/intake/form?appointmentId={id}</c> (US_029, AC-3).
/// <para>
/// <see cref="ManualDraft"/> is the latest incomplete manual record
/// (<c>source = Manual, completedAt IS NULL</c>); <c>null</c> when none exists.
/// <see cref="AiExtracted"/> is the completed AI-sourced record for the same appointment;
/// <c>null</c> when no AI intake has been submitted for this appointment.
/// </para>
/// </summary>
public sealed record IntakeFormResponseDto(
    Guid AppointmentId,
    IntakeDraftDataDto? ManualDraft,
    IntakeDraftDataDto? AiExtracted);
