using Propel.Modules.AI.Models;
using Propel.Modules.Calendar.Dtos;

namespace Propel.Api.Gateway.Infrastructure;

/// <summary>
/// Maps internal degradation result types to <see cref="DegradationNotice"/> DTOs
/// for inclusion in API responses (US_052, AC-3, AC-4, NFR-018).
/// <para>
/// Translates <see cref="CalendarSyncResult.Failed"/> and
/// <see cref="ExtractionResult.ManualFallbackResult"/> into consistent notices that tell
/// the frontend which feature degraded and what fallback is available.
/// Controllers call the appropriate factory method and append non-null results to the
/// <c>DegradationNotices</c> array in the response body.
/// </para>
/// </summary>
public static class DegradationResponseFactory
{
    /// <summary>
    /// Returns a <see cref="DegradationNotice"/> when <paramref name="result"/> is
    /// <see cref="CalendarSyncResult.Failed"/>; returns <c>null</c> when synced or when
    /// no sync was attempted (<paramref name="result"/> is <c>null</c>).
    /// </summary>
    public static DegradationNotice? FromCalendarSyncResult(CalendarSyncResult? result)
        => result is CalendarSyncResult.Failed f
            ? new DegradationNotice(
                Feature: "CalendarSync",
                Message: f.Reason,
                FallbackAvailable: true,
                FallbackType: "IcsDownload")
            : null;

    /// <summary>
    /// Returns a <see cref="DegradationNotice"/> when <paramref name="result"/> is a
    /// <see cref="ExtractionResult.ManualFallbackResult"/> (circuit breaker open).
    /// Returns <c>null</c> for all other result types.
    /// </summary>
    public static DegradationNotice? FromExtractionResult(ExtractionResult result)
        => result is ExtractionResult.ManualFallbackResult mf
            ? new DegradationNotice(
                Feature: "AiExtraction",
                Message: "AI processing temporarily unavailable \u2014 manual review required",
                FallbackAvailable: true,
                FallbackType: "ManualReview")
            : null;
}
