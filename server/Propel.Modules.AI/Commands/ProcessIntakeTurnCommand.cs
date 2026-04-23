using MediatR;
using Propel.Modules.AI.Dtos;

namespace Propel.Modules.AI.Commands;

/// <summary>
/// MediatR command to process a single patient utterance in an AI intake session
/// (US_028, AC-2, AC-3, AIR-O02).
/// <para>
/// <c>PatientId</c> is always resolved from the JWT claim in <c>AiIntakeController</c>
/// before being placed into this command — never from the request body (OWASP A01).
/// </para>
/// </summary>
public sealed record ProcessIntakeTurnCommand(
    Guid SessionId,
    Guid PatientId,
    string UserMessage) : IRequest<AiTurnResponseDto>;
