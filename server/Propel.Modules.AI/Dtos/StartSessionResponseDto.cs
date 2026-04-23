namespace Propel.Modules.AI.Dtos;

/// <summary>
/// Response body for <c>POST /api/intake/ai/session</c> (US_028, AC-1).
/// Returns the newly created <c>sessionId</c> (GUID) the frontend must include
/// in all subsequent message and submit requests.
/// </summary>
public sealed record StartSessionResponseDto(Guid SessionId);
