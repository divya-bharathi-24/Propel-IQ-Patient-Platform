namespace Propel.Modules.AI.Dtos;

/// <summary>
/// Request body for <c>POST /api/intake/ai/session</c> (US_028, AC-1).
/// <c>PatientId</c> is NOT included — it is always resolved from the JWT claim
/// inside <c>AiIntakeController</c> (OWASP A01).
/// </summary>
public sealed record StartSessionRequestDto(Guid AppointmentId);
