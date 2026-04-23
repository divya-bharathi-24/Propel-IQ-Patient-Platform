using System.Text.Json;
using MediatR;

namespace Propel.Modules.Patient.Commands;

/// <summary>
/// MediatR command to autosave a partial manual intake draft (US_029, AC-3 edge case).
/// <para>
/// Performs an UPSERT restricted to <c>source = Manual, completedAt IS NULL</c>:
/// creates a new <see cref="Domain.Entities.IntakeRecord"/> if none exists, or updates the
/// four JSONB columns on the existing draft. Does NOT modify <c>completedAt</c>.
/// </para>
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller — never from
/// the request body or URL (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record AutosaveIntakeCommand(
    Guid AppointmentId,
    Guid PatientId,
    JsonDocument? Demographics,
    JsonDocument? MedicalHistory,
    JsonDocument? Symptoms,
    JsonDocument? Medications
) : IRequest<Unit>;
