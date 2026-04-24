namespace Propel.Modules.Calendar.Dtos;

/// <summary>
/// API response DTO surfacing a degraded feature state to the frontend (US_052, AC-3, AC-4, NFR-018).
/// <para>
/// Included in booking confirmation responses (and any other response where an enrichment step
/// is deferred) so that staff and patients see the correct user-facing message and know whether
/// a fallback action (e.g. ICS download or manual document review) is available.
/// </para>
/// </summary>
/// <param name="Feature">
/// Logical feature name that degraded. Well-known values: <c>"CalendarSync"</c>, <c>"AiExtraction"</c>.
/// </param>
/// <param name="Message">
/// Human-readable message safe for display in the UI. Must not contain patient PII.
/// </param>
/// <param name="FallbackAvailable">
/// <c>true</c> when a compensating action (ICS download, manual review) is available.
/// </param>
/// <param name="FallbackType">
/// Indicates the type of fallback available.
/// Well-known values: <c>"IcsDownload"</c>, <c>"ManualReview"</c>. <c>null</c> when none.
/// </param>
public sealed record DegradationNotice(
    string Feature,
    string Message,
    bool FallbackAvailable,
    string? FallbackType);
