namespace Propel.Modules.AI.Dtos;

/// <summary>
/// Request body for <c>POST /api/intake/ai/submit</c> (US_028, AC-4).
/// All field data is loaded from the in-memory <c>IntakeSessionStore</c> using
/// <c>SessionId</c> — no PHI is accepted via the request body to prevent injection
/// of unverified data (OWASP A03).
/// </summary>
public sealed record SubmitIntakeRequestDto(Guid SessionId);
