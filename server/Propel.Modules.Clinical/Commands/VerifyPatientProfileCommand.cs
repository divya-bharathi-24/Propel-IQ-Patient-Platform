using MediatR;

namespace Propel.Modules.Clinical.Commands;

/// <summary>
/// MediatR command to verify a patient's 360-degree profile (AC-3).
/// Staff <c>userId</c> is sourced from the JWT claim in the controller — never from the request body (OWASP A01).
/// </summary>
public sealed record VerifyPatientProfileCommand(
    Guid PatientId,
    Guid StaffUserId) : IRequest;
