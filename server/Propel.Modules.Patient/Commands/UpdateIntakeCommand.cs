using System.Text.Json;
using MediatR;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Commands;

/// <summary>
/// MediatR command for the full UPSERT of the patient's <see cref="Domain.Entities.IntakeRecord"/>
/// (US_017, AC-2, AC-3 edge case — concurrent edit returns 409).
/// <para>
/// Processing order in the handler:
/// <list type="number">
///   <item>Optimistic concurrency check via <c>RowVersion</c> (Base64-decoded from <c>If-Match</c>
///         header). Throws <see cref="Exceptions.IntakeConcurrencyConflictException"/> (→ 409)
///         when <c>RowVersion</c> does not match the server-side record.</item>
///   <item>FluentValidation of required demographic fields. When fields are missing: persists
///         form data to <c>draftData</c> (AC-3), throws
///         <see cref="Exceptions.IntakeMissingFieldsException"/> (→ 422).</item>
///   <item>UPSERT: UPDATE existing record (setting <c>completedAt</c>, clearing <c>draftData</c>)
///         or INSERT a new record when none exists. Never creates a duplicate row (DR-004).</item>
/// </list>
/// </para>
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller — never from
/// the request body or URL (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record UpdateIntakeCommand(
    Guid AppointmentId,
    Guid PatientId,
    /// <summary>Base64-encoded <c>xmin</c> row version from the <c>If-Match</c> request header.</summary>
    string? RowVersion,
    string? CorrelationId,
    string? IpAddress,
    JsonDocument? Demographics,
    JsonDocument? MedicalHistory,
    JsonDocument? Symptoms,
    JsonDocument? Medications
) : IRequest<UpdateIntakeResult>;

/// <summary>Handler result containing the updated intake DTO and its refreshed ETag.</summary>
public sealed record UpdateIntakeResult(IntakeRecordDto Intake, string ETag);
