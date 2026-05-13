namespace Propel.Modules.AI.Dtos;

/// <summary>
/// Response body for <c>POST /api/intake/ai/session</c> (US_028, AC-1).
/// Returns the newly created <c>sessionId</c> and the AI's <c>openingQuestion</c>
/// that the frontend displays as the first assistant message in the chat.
/// </summary>
public sealed record StartSessionResponseDto(Guid SessionId, string OpeningQuestion);
