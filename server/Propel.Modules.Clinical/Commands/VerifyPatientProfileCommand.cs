using MediatR;
using Propel.Modules.Clinical.Queries;

namespace Propel.Modules.Clinical.Commands;

/// <summary>
/// MediatR command to verify a patient's 360-degree profile (AC-3).
/// Returns <see cref="VerifyProfileResponseDto"/> with the recorded verification timestamp and
/// staff name, so the frontend can display the confirmation without a second round-trip.
/// Staff <c>userId</c> is sourced from the JWT claim in the controller — never from the request body (OWASP A01).
/// </summary>
public sealed record VerifyPatientProfileCommand(
    Guid PatientId,
    Guid StaffUserId) : IRequest<VerifyProfileResponseDto>;
