using System.Text.Json;
using MediatR;

namespace Propel.Modules.Patient.Commands;

/// <summary>
/// MediatR command for the final submission of a completed manual intake record (US_029, AC-3).
/// <para>
/// Processing order in the handler:
/// <list type="number">
///   <item>FluentValidation of required fields (Demographics: fullName, dateOfBirth, phone;
///         Symptoms: at least one entry). On failure → throws
///         <see cref="Exceptions.IntakeMissingFieldsException"/> (→ HTTP 422).</item>
///   <item>UPSERT restricted to <c>source = Manual, completedAt IS NULL</c>: INSERT or UPDATE
///         the draft and set <c>completedAt = UtcNow</c>.</item>
///   <item>Audit log: immutable <c>IntakeCompleted</c> entry (FR-057).</item>
/// </list>
/// </para>
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller — never from
/// the request body or URL (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record SubmitIntakeCommand(
    Guid AppointmentId,
    Guid PatientId,
    JsonDocument? Demographics,
    JsonDocument? MedicalHistory,
    JsonDocument? Symptoms,
    JsonDocument? Medications
) : IRequest<Unit>;
