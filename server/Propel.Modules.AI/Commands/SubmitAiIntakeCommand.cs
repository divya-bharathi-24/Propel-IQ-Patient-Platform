using MediatR;

namespace Propel.Modules.AI.Commands;

/// <summary>
/// Result of a successful AI intake submission (US_028, AC-4).
/// </summary>
public sealed record SubmitAiIntakeResult(Guid IntakeRecordId);

/// <summary>
/// MediatR command to finalise an AI intake session and persist an <c>IntakeRecord</c>
/// (US_028, AC-4).
/// <para>
/// <c>PatientId</c> is always resolved from the JWT claim in <c>AiIntakeController</c>
/// before being placed into this command — never from the request body (OWASP A01).
/// </para>
/// </summary>
public sealed record SubmitAiIntakeCommand(
    Guid SessionId,
    Guid PatientId) : IRequest<SubmitAiIntakeResult>;
