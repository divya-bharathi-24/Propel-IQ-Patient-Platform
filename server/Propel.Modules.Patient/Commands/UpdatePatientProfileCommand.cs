using MediatR;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Commands;

/// <summary>
/// MediatR command to apply a partial update to the authenticated patient's demographic profile
/// (US_015, AC-2, AC-3, AC-4).
/// <para>
/// <c>PatientId</c> is extracted from the JWT claim in the controller — never from the request body
/// (OWASP A01 — Broken Access Control).
/// </para>
/// <para>
/// Locked fields (<c>Name</c>, <c>DateOfBirth</c>, <c>BiologicalSex</c>) are absent from
/// <see cref="Payload"/> and are therefore never applied, regardless of what the client sends (AC-3).
/// </para>
/// </summary>
public sealed record UpdatePatientProfileCommand(
    Guid PatientId,
    string? IfMatchETag,
    string? IpAddress,
    string? CorrelationId,
    UpdatePatientProfileDto Payload
) : IRequest<UpdatePatientProfileResult>;

/// <summary>Handler result containing the updated patient DTO and its refreshed ETag.</summary>
public sealed record UpdatePatientProfileResult(PatientProfileDto Profile, string ETag);
