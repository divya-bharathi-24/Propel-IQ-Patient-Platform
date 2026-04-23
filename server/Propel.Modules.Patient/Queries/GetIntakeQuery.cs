using MediatR;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Queries;

/// <summary>
/// MediatR query to fetch the full <see cref="Domain.Entities.IntakeRecord"/> for a given
/// appointment (US_017, AC-1).
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller — never
/// accepted from the URL or request body — preventing cross-patient data leakage (OWASP A01).
/// </para>
/// Returns a <see cref="GetIntakeResult"/> containing the DTO and the Base64-encoded
/// <c>xmin</c> row version as an ETag for optimistic concurrency on subsequent PUT requests.
/// </summary>
public sealed record GetIntakeQuery(Guid AppointmentId, Guid PatientId) : IRequest<GetIntakeResult>;

/// <summary>Handler result containing the intake DTO and its ETag for the response header.</summary>
public sealed record GetIntakeResult(IntakeRecordDto Intake, string ETag);
