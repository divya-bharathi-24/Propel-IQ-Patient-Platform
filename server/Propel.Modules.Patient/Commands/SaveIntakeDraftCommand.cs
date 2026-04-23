using System.Text.Json;
using MediatR;

namespace Propel.Modules.Patient.Commands;

/// <summary>
/// MediatR command to autosave a partial draft of the patient's intake data (US_017, AC-3, AC-4).
/// <para>
/// Updates only the <c>draftData</c> JSONB column and <c>lastModifiedAt</c> on the existing
/// <see cref="Domain.Entities.IntakeRecord"/>. Does NOT modify <c>completedAt</c>, <c>source</c>,
/// or any of the four primary JSONB intake columns.
/// </para>
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller — never from
/// the request body (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record SaveIntakeDraftCommand(
    Guid AppointmentId,
    Guid PatientId,
    string? CorrelationId,
    JsonDocument DraftData
) : IRequest<Unit>;
