namespace Propel.Modules.AI.Dtos;

/// <summary>
/// Request body for <c>POST /api/intake/ai/message</c> (US_028, AC-2).
/// <c>PatientId</c> is always resolved from the JWT claim (OWASP A01).
/// </summary>
public sealed record IntakeTurnRequestDto(Guid SessionId, string UserMessage);
