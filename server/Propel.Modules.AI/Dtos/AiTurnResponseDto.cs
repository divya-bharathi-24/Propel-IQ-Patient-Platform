namespace Propel.Modules.AI.Dtos;

/// <summary>
/// A single field extracted by the AI NLU layer during a conversation turn (US_028, AC-2, AC-3).
/// Fields with <c>Confidence</c> below 0.8 carry <c>NeedsClarification = true</c> and
/// the AI response will include a targeted follow-up question (AC-3, AIR-003).
/// </summary>
public sealed record ExtractedFieldDto(
    string FieldName,
    string Value,
    double Confidence,
    bool NeedsClarification);

/// <summary>
/// Response body for <c>POST /api/intake/ai/message</c> (US_028, AC-2, AC-3, AIR-O02).
/// <para>
/// Normal path: <c>IsFallback = false</c>; <c>AiResponse</c> and <c>ExtractedFields</c>
/// carry the turn result.
/// </para>
/// <para>
/// Circuit-breaker path: <c>IsFallback = true</c>; <c>PreservedFields</c> carries all
/// fields accumulated so far so the frontend can switch to manual intake without data loss
/// (HTTP 200 — not a 5xx, AIR-O02).
/// </para>
/// </summary>
public sealed record AiTurnResponseDto(
    bool IsFallback,
    string? AiResponse,
    IReadOnlyList<ExtractedFieldDto>? ExtractedFields,
    IReadOnlyList<ExtractedFieldDto>? PreservedFields);
