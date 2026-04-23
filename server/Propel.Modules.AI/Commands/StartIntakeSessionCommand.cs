using MediatR;
using Propel.Modules.AI.Dtos;

namespace Propel.Modules.AI.Commands;

/// <summary>
/// MediatR command to create a new AI intake session (US_028, AC-1).
/// <para>
/// <c>PatientId</c> is always resolved from the JWT claim in <c>AiIntakeController</c>
/// before being placed into this command — never from the request body (OWASP A01).
/// </para>
/// </summary>
public sealed record StartIntakeSessionCommand(
    Guid AppointmentId,
    Guid PatientId) : IRequest<StartSessionResponseDto>;
